using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Tracking
{
    /// <summary>
    /// A single detection from a frame (input to the tracker).
    /// </summary>
    public struct Detection
    {
        public Rect BoundingBox;
        public float Score;
        public string ClassLabel;
        public int ClassId;
    }

    /// <summary>
    /// ByteTrack multi-object tracker.
    /// Key insight: uses ALL detection boxes (including low-confidence ones)
    /// in a two-stage association strategy.
    ///
    /// Reference: "ByteTrack: Multi-Object Tracking by Associating Every Detection Box"
    /// (Zhang et al., ECCV 2022)
    /// </summary>
    public class ByteTrackEngine
    {
        /// <summary>Configuration for ByteTrack.</summary>
        public struct Config
        {
            /// <summary>High confidence threshold for first-stage matching.</summary>
            public float HighThreshold;

            /// <summary>Low confidence threshold (detections below this are discarded entirely).</summary>
            public float LowThreshold;

            /// <summary>IoU threshold for matching (1 - IoU must be below this).</summary>
            public float MatchThreshold;

            /// <summary>Maximum frames a track can be lost before removal.</summary>
            public int MaxLostFrames;

            /// <summary>Minimum consecutive matches for a track to be confirmed.</summary>
            public int MinHits;

            public static Config Default => new Config
            {
                HighThreshold = 0.6f,
                LowThreshold = 0.1f,
                MatchThreshold = 0.8f, // IoU cost threshold (1 - IoU < 0.8 means IoU > 0.2)
                MaxLostFrames = 30,
                MinHits = 3
            };
        }

        readonly Config _config;
        readonly List<Track> _tracks = new List<Track>();
        int _frameCount;

        /// <summary>All currently active tracks (Tracked + Lost + New).</summary>
        public IReadOnlyList<Track> Tracks => _tracks;

        /// <summary>Only confirmed, actively tracked objects.</summary>
        public IEnumerable<Track> ActiveTracks =>
            _tracks.Where(t => t.Status == TrackStatus.Tracked);

        public ByteTrackEngine(Config config = default)
        {
            _config = config.HighThreshold > 0 ? config : Config.Default;
        }

        /// <summary>
        /// Process detections for a new frame and update all tracks.
        /// This is the core ByteTrack algorithm.
        /// </summary>
        /// <param name="detections">All detections in the current frame.</param>
        /// <returns>List of active tracks after update.</returns>
        public List<Track> Update(Detection[] detections)
        {
            _frameCount++;

            // ─── Step 1: Split detections into high and low confidence ───

            var highDets = new List<Detection>();
            var lowDets = new List<Detection>();

            foreach (var det in detections)
            {
                if (det.Score >= _config.HighThreshold)
                    highDets.Add(det);
                else if (det.Score >= _config.LowThreshold)
                    lowDets.Add(det);
                // Below low threshold: discard entirely
            }

            // ─── Step 2: Predict new positions for all tracks ───

            var predictedBoxes = new List<Rect>();
            foreach (var track in _tracks)
            {
                if (track.Status == TrackStatus.Removed)
                    continue;
                predictedBoxes.Add(track.Predict());
            }

            // Get tracks that aren't removed
            var activeTracks = _tracks.Where(t => t.Status != TrackStatus.Removed).ToList();

            // ─── Step 3: First association — high confidence detections ───

            var trackBoxes = activeTracks.Select(t =>
                Track.MeasurementToBbox(t.KalmanFilter.GetState())).ToArray();
            var highDetBoxes = highDets.Select(d => d.BoundingBox).ToArray();

            List<(int, int)> firstMatches;
            List<int> unmatchedTrackIndices;
            List<int> unmatchedHighDetIndices;

            if (trackBoxes.Length > 0 && highDetBoxes.Length > 0)
            {
                var costMatrix = HungarianAlgorithm.BuildIoUCostMatrix(trackBoxes, highDetBoxes);
                (firstMatches, unmatchedTrackIndices, unmatchedHighDetIndices) =
                    HungarianAlgorithm.Solve(costMatrix, _config.MatchThreshold);
            }
            else
            {
                firstMatches = new List<(int, int)>();
                unmatchedTrackIndices = Enumerable.Range(0, activeTracks.Count).ToList();
                unmatchedHighDetIndices = Enumerable.Range(0, highDets.Count).ToList();
            }

            // Apply first matches
            foreach (var (trackIdx, detIdx) in firstMatches)
            {
                var det = highDets[detIdx];
                activeTracks[trackIdx].Update(det.BoundingBox, det.Score, _frameCount);
            }

            // ─── Step 4: Second association — low confidence detections with remaining tracks ───
            // This is ByteTrack's key innovation: even low-score detections are used to maintain
            // existing tracks, preventing ID switches during partial occlusion.

            var remainingTracks = unmatchedTrackIndices
                .Where(i => activeTracks[i].Status == TrackStatus.Tracked)
                .ToList();

            if (remainingTracks.Count > 0 && lowDets.Count > 0)
            {
                var remTrackBoxes = remainingTracks
                    .Select(i => Track.MeasurementToBbox(activeTracks[i].KalmanFilter.GetState()))
                    .ToArray();
                var lowDetBoxes = lowDets.Select(d => d.BoundingBox).ToArray();

                var costMatrix2 = HungarianAlgorithm.BuildIoUCostMatrix(remTrackBoxes, lowDetBoxes);
                var (secondMatches, unmatchedRemaining, _) =
                    HungarianAlgorithm.Solve(costMatrix2, _config.MatchThreshold);

                foreach (var (remIdx, detIdx) in secondMatches)
                {
                    int trackIdx = remainingTracks[remIdx];
                    var det = lowDets[detIdx];
                    activeTracks[trackIdx].Update(det.BoundingBox, det.Score, _frameCount);
                }

                // Update unmatched track indices to only include truly unmatched ones
                var secondMatchedRemIndices = new HashSet<int>(secondMatches.Select(m => m.Item1));
                var stillUnmatched = new List<int>();
                for (int i = 0; i < unmatchedTrackIndices.Count; i++)
                {
                    int origIdx = unmatchedTrackIndices[i];
                    int remListIdx = remainingTracks.IndexOf(origIdx);
                    if (remListIdx < 0 || !secondMatchedRemIndices.Contains(remListIdx))
                        stillUnmatched.Add(origIdx);
                }
                unmatchedTrackIndices = stillUnmatched;
            }

            // ─── Step 5: Mark unmatched tracks as missed ───

            foreach (int idx in unmatchedTrackIndices)
            {
                activeTracks[idx].MarkMissed(_config.MaxLostFrames);
            }

            // ─── Step 6: Create new tracks from unmatched high-confidence detections ───

            foreach (int detIdx in unmatchedHighDetIndices)
            {
                var det = highDets[detIdx];
                var newTrack = new Track(det.BoundingBox, det.Score, _frameCount,
                    det.ClassLabel, det.ClassId);
                _tracks.Add(newTrack);
            }

            // ─── Step 7: Remove dead tracks ───

            _tracks.RemoveAll(t => t.Status == TrackStatus.Removed);

            return _tracks.Where(t => t.Status == TrackStatus.Tracked).ToList();
        }

        /// <summary>Reset the tracker, removing all tracks.</summary>
        public void Reset()
        {
            _tracks.Clear();
            _frameCount = 0;
        }
    }
}

using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Tracking
{
    /// <summary>
    /// Lifecycle state of a tracked object.
    /// </summary>
    public enum TrackStatus
    {
        /// <summary>Newly created, not yet confirmed.</summary>
        New,

        /// <summary>Actively being tracked with regular detections.</summary>
        Tracked,

        /// <summary>Lost detection but still within the grace period.</summary>
        Lost,

        /// <summary>Removed from tracking (exceeded max lost frames).</summary>
        Removed
    }

    /// <summary>
    /// Represents a single tracked object across frames.
    /// Maintains state, ID, bounding box history, and Kalman filter for prediction.
    /// </summary>
    public class Track
    {
        static int s_NextId = 1;

        /// <summary>Unique ID for this track.</summary>
        public int Id { get; private set; }

        /// <summary>Current lifecycle state.</summary>
        public TrackStatus Status { get; set; }

        /// <summary>Detection class label (e.g., "person", "ball").</summary>
        public string ClassLabel { get; set; }

        /// <summary>Detection class ID.</summary>
        public int ClassId { get; set; }

        /// <summary>Last known bounding box [x1, y1, x2, y2].</summary>
        public Rect BoundingBox { get; set; }

        /// <summary>Last detection confidence score.</summary>
        public float Score { get; set; }

        /// <summary>Frame index when this track was created.</summary>
        public int StartFrame { get; private set; }

        /// <summary>Frame index of the last successful detection match.</summary>
        public int LastSeenFrame { get; set; }

        /// <summary>Number of consecutive frames without a detection match.</summary>
        public int FramesSinceSeen { get; set; }

        /// <summary>Total number of frames this track has been matched.</summary>
        public int MatchedFrameCount { get; set; }

        /// <summary>Kalman filter state for this track.</summary>
        public KalmanFilter KalmanFilter { get; private set; }

        public Track(Rect bbox, float score, int frameIndex, string classLabel = "", int classId = 0)
        {
            Id = s_NextId++;
            Status = TrackStatus.New;
            BoundingBox = bbox;
            Score = score;
            StartFrame = frameIndex;
            LastSeenFrame = frameIndex;
            FramesSinceSeen = 0;
            MatchedFrameCount = 1;
            ClassLabel = classLabel;
            ClassId = classId;

            // Initialize Kalman filter with bounding box center + size
            KalmanFilter = new KalmanFilter();
            KalmanFilter.Initiate(BboxToMeasurement(bbox));
        }

        /// <summary>
        /// Predict the next state using the Kalman filter.
        /// Returns the predicted bounding box.
        /// </summary>
        public Rect Predict()
        {
            KalmanFilter.Predict();
            var state = KalmanFilter.GetState();
            return MeasurementToBbox(state);
        }

        /// <summary>
        /// Update the track with a new matched detection.
        /// </summary>
        public void Update(Rect bbox, float score, int frameIndex)
        {
            BoundingBox = bbox;
            Score = score;
            LastSeenFrame = frameIndex;
            FramesSinceSeen = 0;
            MatchedFrameCount++;

            if (Status == TrackStatus.New && MatchedFrameCount >= 3)
                Status = TrackStatus.Tracked;
            else if (Status == TrackStatus.Lost)
                Status = TrackStatus.Tracked;

            KalmanFilter.Update(BboxToMeasurement(bbox));
        }

        /// <summary>
        /// Mark this track as not matched in the current frame.
        /// </summary>
        public void MarkMissed(int maxLostFrames)
        {
            FramesSinceSeen++;
            if (FramesSinceSeen > maxLostFrames)
                Status = TrackStatus.Removed;
            else if (Status == TrackStatus.Tracked)
                Status = TrackStatus.Lost;
        }

        /// <summary>Convert bounding box to Kalman measurement [cx, cy, aspect, height].</summary>
        public static float[] BboxToMeasurement(Rect bbox)
        {
            float cx = bbox.x + bbox.width * 0.5f;
            float cy = bbox.y + bbox.height * 0.5f;
            float aspect = bbox.width / Mathf.Max(bbox.height, 1e-6f);
            float height = bbox.height;
            return new float[] { cx, cy, aspect, height };
        }

        /// <summary>Convert Kalman state [cx, cy, aspect, height] to bounding box.</summary>
        public static Rect MeasurementToBbox(float[] state)
        {
            float cx = state[0];
            float cy = state[1];
            float aspect = state[2];
            float height = Mathf.Max(state[3], 1f);
            float width = aspect * height;
            return new Rect(cx - width * 0.5f, cy - height * 0.5f, width, height);
        }

        /// <summary>Reset the global track ID counter (for testing).</summary>
        public static void ResetIdCounter()
        {
            s_NextId = 1;
        }
    }
}

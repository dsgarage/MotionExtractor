using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Pipeline
{
    /// <summary>
    /// A single keypoint detected by BlazePose.
    /// </summary>
    public struct Keypoint
    {
        /// <summary>Position in image coordinates (pixels). Z is relative depth from hip.</summary>
        public Vector3 Position;

        /// <summary>Visibility score [0,1]. Higher means the keypoint is likely visible.</summary>
        public float Visibility;

        /// <summary>Presence score [0,1]. Higher means the keypoint is likely present in the image.</summary>
        public float Presence;

        public bool IsVisible => Visibility > 0.5f;
        public bool IsPresent => Presence > 0.5f;
    }

    /// <summary>
    /// Result of pose estimation for a single person in a single frame.
    /// Contains 33 BlazePose keypoints.
    /// </summary>
    public class PoseResult
    {
        /// <summary>33 keypoints in BlazePose order.</summary>
        public Keypoint[] Keypoints;

        /// <summary>Detection confidence score [0,1].</summary>
        public float Score;

        /// <summary>Whether the detection is valid (score above threshold).</summary>
        public bool IsValid;

        public PoseResult()
        {
            Keypoints = new Keypoint[SkeletonDefinitions.KeypointCount];
            Score = 0f;
            IsValid = false;
        }

        /// <summary>
        /// Get 3D positions as a Vector3 array (convenience accessor).
        /// </summary>
        public Vector3[] GetPositions()
        {
            var positions = new Vector3[Keypoints.Length];
            for (int i = 0; i < Keypoints.Length; i++)
                positions[i] = Keypoints[i].Position;
            return positions;
        }
    }

    /// <summary>
    /// Pose data for a single frame in a time series.
    /// </summary>
    public class PoseFrame
    {
        /// <summary>Time in seconds from the start of the video.</summary>
        public float Timestamp;

        /// <summary>Frame index in the video.</summary>
        public int FrameIndex;

        /// <summary>Pose results for each detected person in this frame.</summary>
        public PoseResult[] Poses;

        /// <summary>Source image dimensions.</summary>
        public Vector2Int ImageSize;
    }
}

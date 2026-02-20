using UnityEngine;

namespace DSGarage.MotionExtractor
{
    /// <summary>
    /// BlazePose 33-keypoint skeleton definition and Unity Humanoid bone mapping.
    /// </summary>
    public static class SkeletonDefinitions
    {
        // ─── BlazePose 33 Keypoint Indices ───

        public const int Nose = 0;
        public const int LeftEyeInner = 1;
        public const int LeftEye = 2;
        public const int LeftEyeOuter = 3;
        public const int RightEyeInner = 4;
        public const int RightEye = 5;
        public const int RightEyeOuter = 6;
        public const int LeftEar = 7;
        public const int RightEar = 8;
        public const int MouthLeft = 9;
        public const int MouthRight = 10;
        public const int LeftShoulder = 11;
        public const int RightShoulder = 12;
        public const int LeftElbow = 13;
        public const int RightElbow = 14;
        public const int LeftWrist = 15;
        public const int RightWrist = 16;
        public const int LeftPinky = 17;
        public const int RightPinky = 18;
        public const int LeftIndex = 19;
        public const int RightIndex = 20;
        public const int LeftThumb = 21;
        public const int RightThumb = 22;
        public const int LeftHip = 23;
        public const int RightHip = 24;
        public const int LeftKnee = 25;
        public const int RightKnee = 26;
        public const int LeftAnkle = 27;
        public const int RightAnkle = 28;
        public const int LeftHeel = 29;
        public const int RightHeel = 30;
        public const int LeftFootIndex = 31;
        public const int RightFootIndex = 32;

        public const int KeypointCount = 33;

        // ─── Skeleton Connections (for overlay drawing) ───

        public static readonly (int from, int to)[] Connections = new (int, int)[]
        {
            // Face
            (Nose, LeftEyeInner), (LeftEyeInner, LeftEye), (LeftEye, LeftEyeOuter),
            (Nose, RightEyeInner), (RightEyeInner, RightEye), (RightEye, RightEyeOuter),
            (LeftEar, LeftEyeOuter), (RightEar, RightEyeOuter),
            (MouthLeft, MouthRight),

            // Torso
            (LeftShoulder, RightShoulder),
            (LeftShoulder, LeftHip),
            (RightShoulder, RightHip),
            (LeftHip, RightHip),

            // Left arm
            (LeftShoulder, LeftElbow), (LeftElbow, LeftWrist),
            (LeftWrist, LeftPinky), (LeftWrist, LeftIndex), (LeftWrist, LeftThumb),

            // Right arm
            (RightShoulder, RightElbow), (RightElbow, RightWrist),
            (RightWrist, RightPinky), (RightWrist, RightIndex), (RightWrist, RightThumb),

            // Left leg
            (LeftHip, LeftKnee), (LeftKnee, LeftAnkle),
            (LeftAnkle, LeftHeel), (LeftAnkle, LeftFootIndex),

            // Right leg
            (RightHip, RightKnee), (RightKnee, RightAnkle),
            (RightAnkle, RightHeel), (RightAnkle, RightFootIndex),
        };

        // ─── BlazePose → Unity HumanBodyBones Mapping ───

        public static readonly (int blazePoseIndex, HumanBodyBones bone)[] HumanoidMapping =
            new (int, HumanBodyBones)[]
        {
            (LeftShoulder,  HumanBodyBones.LeftUpperArm),
            (RightShoulder, HumanBodyBones.RightUpperArm),
            (LeftElbow,     HumanBodyBones.LeftLowerArm),
            (RightElbow,    HumanBodyBones.RightLowerArm),
            (LeftWrist,     HumanBodyBones.LeftHand),
            (RightWrist,    HumanBodyBones.RightHand),
            (LeftHip,       HumanBodyBones.LeftUpperLeg),
            (RightHip,      HumanBodyBones.RightUpperLeg),
            (LeftKnee,      HumanBodyBones.LeftLowerLeg),
            (RightKnee,     HumanBodyBones.RightLowerLeg),
            (LeftAnkle,     HumanBodyBones.LeftFoot),
            (RightAnkle,    HumanBodyBones.RightFoot),
            (LeftFootIndex, HumanBodyBones.LeftToes),
            (RightFootIndex,HumanBodyBones.RightToes),
        };

        /// <summary>
        /// Compute the midpoint of two keypoints (used for estimating Spine, Chest, Neck, Hips, Head).
        /// </summary>
        public static Vector3 Midpoint(Vector3 a, Vector3 b)
        {
            return (a + b) * 0.5f;
        }

        /// <summary>
        /// Estimate Hips center from left/right hip keypoints.
        /// </summary>
        public static Vector3 EstimateHips(Vector3[] keypoints)
        {
            return Midpoint(keypoints[LeftHip], keypoints[RightHip]);
        }

        /// <summary>
        /// Estimate Spine from hips center and shoulder center.
        /// Returns a point 1/3 of the way from hips to shoulders.
        /// </summary>
        public static Vector3 EstimateSpine(Vector3[] keypoints)
        {
            var hips = EstimateHips(keypoints);
            var shoulders = Midpoint(keypoints[LeftShoulder], keypoints[RightShoulder]);
            return Vector3.Lerp(hips, shoulders, 0.33f);
        }

        /// <summary>
        /// Estimate Chest from hips center and shoulder center.
        /// Returns a point 2/3 of the way from hips to shoulders.
        /// </summary>
        public static Vector3 EstimateChest(Vector3[] keypoints)
        {
            var hips = EstimateHips(keypoints);
            var shoulders = Midpoint(keypoints[LeftShoulder], keypoints[RightShoulder]);
            return Vector3.Lerp(hips, shoulders, 0.67f);
        }

        /// <summary>
        /// Estimate Neck from shoulder center and nose.
        /// </summary>
        public static Vector3 EstimateNeck(Vector3[] keypoints)
        {
            var shoulders = Midpoint(keypoints[LeftShoulder], keypoints[RightShoulder]);
            return Vector3.Lerp(shoulders, keypoints[Nose], 0.4f);
        }

        /// <summary>
        /// Estimate Head position from nose and ears.
        /// </summary>
        public static Vector3 EstimateHead(Vector3[] keypoints)
        {
            return Midpoint(keypoints[LeftEar], keypoints[RightEar]);
        }
    }
}

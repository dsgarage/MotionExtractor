using System;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Pipeline
{
    /// <summary>
    /// One Euro Filter for smoothing noisy time-series data.
    /// Provides adaptive low-pass filtering that minimizes both jitter and lag.
    ///
    /// Reference: "1€ Filter: A Simple Speed-based Low-pass Filter for Noisy Input in
    /// Interactive Systems" (Casiez et al., CHI 2012)
    /// </summary>
    public class OneEuroFilter
    {
        readonly float _minCutoff;
        readonly float _beta;
        readonly float _dCutoff;

        LowPassFilter _xFilter;
        LowPassFilter _dxFilter;

        float _lastTimestamp;
        bool _initialized;

        /// <summary>
        /// Create a One Euro Filter.
        /// </summary>
        /// <param name="minCutoff">Minimum cutoff frequency (Hz). Lower = more smoothing at low speed. Default: 1.0</param>
        /// <param name="beta">Speed coefficient. Higher = less lag at high speed. Default: 0.0</param>
        /// <param name="dCutoff">Derivative cutoff frequency (Hz). Default: 1.0</param>
        public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _dCutoff = dCutoff;
        }

        /// <summary>Filter a new value.</summary>
        /// <param name="value">The noisy input value.</param>
        /// <param name="timestamp">Current timestamp in seconds.</param>
        /// <returns>The filtered value.</returns>
        public float Filter(float value, float timestamp)
        {
            if (!_initialized)
            {
                _xFilter = new LowPassFilter(value);
                _dxFilter = new LowPassFilter(0);
                _lastTimestamp = timestamp;
                _initialized = true;
                return value;
            }

            float dt = timestamp - _lastTimestamp;
            if (dt <= 0) dt = 1f / 30f; // fallback
            _lastTimestamp = timestamp;

            // Estimate derivative
            float dx = (value - _xFilter.LastValue) / dt;
            float alpha_d = ComputeAlpha(dt, _dCutoff);
            float filteredDx = _dxFilter.Apply(dx, alpha_d);

            // Adaptive cutoff based on speed
            float cutoff = _minCutoff + _beta * Mathf.Abs(filteredDx);
            float alpha = ComputeAlpha(dt, cutoff);

            return _xFilter.Apply(value, alpha);
        }

        /// <summary>Reset filter state.</summary>
        public void Reset()
        {
            _initialized = false;
        }

        static float ComputeAlpha(float dt, float cutoff)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }

        struct LowPassFilter
        {
            public float LastValue;
            bool _initialized;

            public LowPassFilter(float initialValue)
            {
                LastValue = initialValue;
                _initialized = true;
            }

            public float Apply(float value, float alpha)
            {
                if (!_initialized)
                {
                    LastValue = value;
                    _initialized = true;
                    return value;
                }

                LastValue = alpha * value + (1f - alpha) * LastValue;
                return LastValue;
            }
        }
    }

    /// <summary>
    /// 3D One Euro Filter for smoothing Vector3 time-series (e.g., joint positions).
    /// Applies independent One Euro Filters to each axis.
    /// </summary>
    public class OneEuroFilter3D
    {
        readonly OneEuroFilter _xFilter;
        readonly OneEuroFilter _yFilter;
        readonly OneEuroFilter _zFilter;

        public OneEuroFilter3D(float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
        {
            _xFilter = new OneEuroFilter(minCutoff, beta, dCutoff);
            _yFilter = new OneEuroFilter(minCutoff, beta, dCutoff);
            _zFilter = new OneEuroFilter(minCutoff, beta, dCutoff);
        }

        public Vector3 Filter(Vector3 value, float timestamp)
        {
            return new Vector3(
                _xFilter.Filter(value.x, timestamp),
                _yFilter.Filter(value.y, timestamp),
                _zFilter.Filter(value.z, timestamp)
            );
        }

        public void Reset()
        {
            _xFilter.Reset();
            _yFilter.Reset();
            _zFilter.Reset();
        }
    }

    /// <summary>
    /// Applies OneEuroFilter3D to all 33 BlazePose keypoints.
    /// </summary>
    public class SkeletonSmoother
    {
        readonly OneEuroFilter3D[] _filters;

        /// <summary>
        /// Create a skeleton smoother for 33 keypoints.
        /// </summary>
        /// <param name="minCutoff">Minimum cutoff (Hz). Lower = smoother. Recommended: 0.5-2.0</param>
        /// <param name="beta">Speed coefficient. Higher = less lag. Recommended: 0.0-1.0</param>
        /// <param name="dCutoff">Derivative cutoff. Default: 1.0</param>
        public SkeletonSmoother(float minCutoff = 1.0f, float beta = 0.5f, float dCutoff = 1.0f)
        {
            _filters = new OneEuroFilter3D[SkeletonDefinitions.KeypointCount];
            for (int i = 0; i < _filters.Length; i++)
                _filters[i] = new OneEuroFilter3D(minCutoff, beta, dCutoff);
        }

        /// <summary>
        /// Smooth a frame of keypoint positions.
        /// </summary>
        /// <param name="positions">33 keypoint positions.</param>
        /// <param name="timestamp">Current timestamp in seconds.</param>
        /// <returns>Smoothed positions.</returns>
        public Vector3[] Smooth(Vector3[] positions, float timestamp)
        {
            if (positions.Length != _filters.Length)
                throw new ArgumentException(
                    $"Expected {_filters.Length} positions, got {positions.Length}");

            var smoothed = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                smoothed[i] = _filters[i].Filter(positions[i], timestamp);
            return smoothed;
        }

        public void Reset()
        {
            foreach (var f in _filters)
                f.Reset();
        }
    }
}

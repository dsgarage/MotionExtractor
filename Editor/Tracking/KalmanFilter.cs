using System;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Tracking
{
    /// <summary>
    /// Linear Kalman filter for bounding box tracking.
    /// State vector: [cx, cy, aspect_ratio, height, vx, vy, va, vh] (8D).
    /// Measurement vector: [cx, cy, aspect_ratio, height] (4D).
    /// Based on SORT/ByteTrack Kalman filter implementation.
    /// </summary>
    public class KalmanFilter
    {
        const int StateDim = 8;
        const int MeasDim = 4;

        // Standard deviation weights for position and velocity
        const float StdWeightPosition = 1f / 20f;
        const float StdWeightVelocity = 1f / 160f;

        /// <summary>State vector [cx, cy, a, h, vx, vy, va, vh].</summary>
        float[] _x;

        /// <summary>State covariance matrix (8x8).</summary>
        float[,] _P;

        /// <summary>State transition matrix (8x8).</summary>
        readonly float[,] _F;

        /// <summary>Measurement matrix (4x8).</summary>
        readonly float[,] _H;

        bool _initialized;

        public KalmanFilter()
        {
            _x = new float[StateDim];
            _P = new float[StateDim, StateDim];

            // State transition (constant velocity model)
            _F = Identity(StateDim);
            for (int i = 0; i < MeasDim; i++)
                _F[i, i + MeasDim] = 1f; // velocity components

            // Measurement matrix
            _H = new float[MeasDim, StateDim];
            for (int i = 0; i < MeasDim; i++)
                _H[i, i] = 1f;
        }

        /// <summary>Initialize the filter with the first measurement.</summary>
        public void Initiate(float[] measurement)
        {
            if (measurement.Length != MeasDim)
                throw new ArgumentException($"Measurement must be {MeasDim}D");

            for (int i = 0; i < MeasDim; i++)
                _x[i] = measurement[i];
            for (int i = MeasDim; i < StateDim; i++)
                _x[i] = 0f; // zero initial velocity

            // Initial covariance
            float h = measurement[3]; // height
            float[] std = new float[]
            {
                2f * StdWeightPosition * h,
                2f * StdWeightPosition * h,
                1e-2f,
                2f * StdWeightPosition * h,
                10f * StdWeightVelocity * h,
                10f * StdWeightVelocity * h,
                1e-5f,
                10f * StdWeightVelocity * h
            };

            _P = new float[StateDim, StateDim];
            for (int i = 0; i < StateDim; i++)
                _P[i, i] = std[i] * std[i];

            _initialized = true;
        }

        /// <summary>Predict the next state.</summary>
        public void Predict()
        {
            if (!_initialized) return;

            float h = _x[3]; // current height estimate

            // Process noise
            float[] stdQ = new float[]
            {
                StdWeightPosition * h,
                StdWeightPosition * h,
                1e-2f,
                StdWeightPosition * h,
                StdWeightVelocity * h,
                StdWeightVelocity * h,
                1e-5f,
                StdWeightVelocity * h
            };

            // x = F * x (state prediction)
            var xNew = new float[StateDim];
            for (int i = 0; i < StateDim; i++)
            {
                float sum = 0;
                for (int j = 0; j < StateDim; j++)
                    sum += _F[i, j] * _x[j];
                xNew[i] = sum;
            }
            _x = xNew;

            // P = F * P * F^T + Q
            var FP = MatMul(_F, _P, StateDim, StateDim, StateDim);
            var FT = Transpose(_F, StateDim, StateDim);
            _P = MatMul(FP, FT, StateDim, StateDim, StateDim);

            for (int i = 0; i < StateDim; i++)
                _P[i, i] += stdQ[i] * stdQ[i];
        }

        /// <summary>Update the state with a new measurement.</summary>
        public void Update(float[] measurement)
        {
            if (!_initialized) return;
            if (measurement.Length != MeasDim)
                throw new ArgumentException($"Measurement must be {MeasDim}D");

            float h = _x[3];

            // Measurement noise
            float[] stdR = new float[]
            {
                StdWeightPosition * h,
                StdWeightPosition * h,
                1e-1f,
                StdWeightPosition * h
            };

            // Innovation: y = z - H * x
            var y = new float[MeasDim];
            for (int i = 0; i < MeasDim; i++)
            {
                float hx = 0;
                for (int j = 0; j < StateDim; j++)
                    hx += _H[i, j] * _x[j];
                y[i] = measurement[i] - hx;
            }

            // Innovation covariance: S = H * P * H^T + R
            var HP = MatMul(_H, _P, MeasDim, StateDim, StateDim);
            var HT = Transpose(_H, MeasDim, StateDim);
            var S = MatMul(HP, HT, MeasDim, StateDim, MeasDim);
            for (int i = 0; i < MeasDim; i++)
                S[i, i] += stdR[i] * stdR[i];

            // Kalman gain: K = P * H^T * S^{-1}
            var PHT = MatMul(_P, HT, StateDim, StateDim, MeasDim);
            var SInv = Invert4x4(S);
            var K = MatMul(PHT, SInv, StateDim, MeasDim, MeasDim);

            // State update: x = x + K * y
            for (int i = 0; i < StateDim; i++)
            {
                float ky = 0;
                for (int j = 0; j < MeasDim; j++)
                    ky += K[i, j] * y[j];
                _x[i] += ky;
            }

            // Covariance update: P = (I - K * H) * P
            var KH = MatMul(K, _H, StateDim, MeasDim, StateDim);
            var I_KH = Identity(StateDim);
            for (int i = 0; i < StateDim; i++)
                for (int j = 0; j < StateDim; j++)
                    I_KH[i, j] -= KH[i, j];

            _P = MatMul(I_KH, _P, StateDim, StateDim, StateDim);
        }

        /// <summary>Get current state estimate [cx, cy, aspect, height, ...].</summary>
        public float[] GetState()
        {
            return (float[])_x.Clone();
        }

        // ─── Matrix Utilities ───

        static float[,] Identity(int n)
        {
            var m = new float[n, n];
            for (int i = 0; i < n; i++) m[i, i] = 1f;
            return m;
        }

        static float[,] MatMul(float[,] a, float[,] b, int rows, int inner, int cols)
        {
            var result = new float[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    float sum = 0;
                    for (int k = 0; k < inner; k++)
                        sum += a[i, k] * b[k, j];
                    result[i, j] = sum;
                }
            return result;
        }

        static float[,] Transpose(float[,] m, int rows, int cols)
        {
            var result = new float[cols, rows];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[j, i] = m[i, j];
            return result;
        }

        /// <summary>Invert a 4x4 matrix using cofactor expansion.</summary>
        static float[,] Invert4x4(float[,] m)
        {
            int n = 4;
            var result = new float[n, n];
            var augmented = new float[n, 2 * n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    augmented[i, j] = m[i, j];
                augmented[i, i + n] = 1f;
            }

            // Gauss-Jordan elimination
            for (int col = 0; col < n; col++)
            {
                // Find pivot
                int pivotRow = col;
                float maxVal = Mathf.Abs(augmented[col, col]);
                for (int row = col + 1; row < n; row++)
                {
                    float absVal = Mathf.Abs(augmented[row, col]);
                    if (absVal > maxVal)
                    {
                        maxVal = absVal;
                        pivotRow = row;
                    }
                }

                // Swap rows
                if (pivotRow != col)
                {
                    for (int j = 0; j < 2 * n; j++)
                    {
                        float tmp = augmented[col, j];
                        augmented[col, j] = augmented[pivotRow, j];
                        augmented[pivotRow, j] = tmp;
                    }
                }

                float pivot = augmented[col, col];
                if (Mathf.Abs(pivot) < 1e-10f)
                {
                    Debug.LogWarning("[KalmanFilter] Singular matrix in inversion, using pseudoinverse.");
                    pivot = 1e-10f;
                }

                // Scale pivot row
                for (int j = 0; j < 2 * n; j++)
                    augmented[col, j] /= pivot;

                // Eliminate column
                for (int row = 0; row < n; row++)
                {
                    if (row == col) continue;
                    float factor = augmented[row, col];
                    for (int j = 0; j < 2 * n; j++)
                        augmented[row, j] -= factor * augmented[col, j];
                }
            }

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    result[i, j] = augmented[i, j + n];

            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DSGarage.MotionExtractor.Editor.Tracking
{
    /// <summary>
    /// Hungarian algorithm (Munkres) for optimal assignment in a cost matrix.
    /// Used by ByteTrack for matching detections to existing tracks via IoU cost.
    /// </summary>
    public static class HungarianAlgorithm
    {
        /// <summary>
        /// Solve the linear assignment problem.
        /// Finds the optimal assignment that minimizes total cost.
        /// </summary>
        /// <param name="costMatrix">Cost matrix [rows x cols]. Rows = tracks, Cols = detections.</param>
        /// <returns>
        /// List of (row, col) matched pairs, list of unmatched row indices, list of unmatched col indices.
        /// </returns>
        public static (List<(int row, int col)> matches, List<int> unmatchedRows, List<int> unmatchedCols)
            Solve(float[,] costMatrix, float maxCost = float.MaxValue)
        {
            int rows = costMatrix.GetLength(0);
            int cols = costMatrix.GetLength(1);

            var matches = new List<(int, int)>();
            var unmatchedRows = new List<int>();
            var unmatchedCols = new List<int>();

            if (rows == 0 || cols == 0)
            {
                for (int i = 0; i < rows; i++) unmatchedRows.Add(i);
                for (int j = 0; j < cols; j++) unmatchedCols.Add(j);
                return (matches, unmatchedRows, unmatchedCols);
            }

            // Run Munkres algorithm
            int[] assignment = MunkresAssign(costMatrix, rows, cols);

            // Collect results, filtering by maxCost
            var matchedRows = new HashSet<int>();
            var matchedCols = new HashSet<int>();

            for (int i = 0; i < rows; i++)
            {
                if (assignment[i] >= 0 && assignment[i] < cols &&
                    costMatrix[i, assignment[i]] <= maxCost)
                {
                    matches.Add((i, assignment[i]));
                    matchedRows.Add(i);
                    matchedCols.Add(assignment[i]);
                }
            }

            for (int i = 0; i < rows; i++)
                if (!matchedRows.Contains(i))
                    unmatchedRows.Add(i);

            for (int j = 0; j < cols; j++)
                if (!matchedCols.Contains(j))
                    unmatchedCols.Add(j);

            return (matches, unmatchedRows, unmatchedCols);
        }

        /// <summary>
        /// Compute IoU (Intersection over Union) between two bounding boxes.
        /// </summary>
        public static float IoU(Rect a, Rect b)
        {
            float x1 = Mathf.Max(a.xMin, b.xMin);
            float y1 = Mathf.Max(a.yMin, b.yMin);
            float x2 = Mathf.Min(a.xMax, b.xMax);
            float y2 = Mathf.Min(a.yMax, b.yMax);

            float intersection = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
            float union = a.width * a.height + b.width * b.height - intersection;

            return union > 0 ? intersection / union : 0;
        }

        /// <summary>
        /// Build an IoU cost matrix (1 - IoU) between tracks and detections.
        /// </summary>
        public static float[,] BuildIoUCostMatrix(Rect[] trackBoxes, Rect[] detBoxes)
        {
            int rows = trackBoxes.Length;
            int cols = detBoxes.Length;
            var cost = new float[rows, cols];

            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    cost[i, j] = 1f - IoU(trackBoxes[i], detBoxes[j]);

            return cost;
        }

        // ─── Munkres (Hungarian) Algorithm Implementation ───

        static int[] MunkresAssign(float[,] costMatrix, int rows, int cols)
        {
            int n = Mathf.Max(rows, cols);

            // Pad to square matrix
            var C = new float[n, n];
            float padValue = float.MaxValue / 2;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    C[i, j] = (i < rows && j < cols) ? costMatrix[i, j] : padValue;

            // Step 1: Subtract row minimums
            for (int i = 0; i < n; i++)
            {
                float min = C[i, 0];
                for (int j = 1; j < n; j++)
                    if (C[i, j] < min) min = C[i, j];
                for (int j = 0; j < n; j++)
                    C[i, j] -= min;
            }

            // Step 2: Subtract column minimums
            for (int j = 0; j < n; j++)
            {
                float min = C[0, j];
                for (int i = 1; i < n; i++)
                    if (C[i, j] < min) min = C[i, j];
                for (int i = 0; i < n; i++)
                    C[i, j] -= min;
            }

            // Assignments
            var rowAssignment = new int[n];
            var colAssignment = new int[n];
            for (int i = 0; i < n; i++) { rowAssignment[i] = -1; colAssignment[i] = -1; }

            // Step 3: Find initial zeros and try to assign
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (C[i, j] < 1e-6f && rowAssignment[i] == -1 && colAssignment[j] == -1)
                    {
                        rowAssignment[i] = j;
                        colAssignment[j] = i;
                    }
                }
            }

            // Iteratively improve assignment using augmenting paths
            for (int iter = 0; iter < n * n; iter++)
            {
                // Count assignments
                int assignedCount = 0;
                for (int i = 0; i < n; i++)
                    if (rowAssignment[i] >= 0) assignedCount++;

                if (assignedCount == n)
                    break;

                // Mark covered rows and columns
                var coveredRow = new bool[n];
                var coveredCol = new bool[n];
                for (int i = 0; i < n; i++)
                    if (rowAssignment[i] >= 0)
                        coveredCol[rowAssignment[i]] = true;

                // Try to find augmenting paths via BFS
                bool improved = false;
                for (int i = 0; i < n; i++)
                {
                    if (rowAssignment[i] >= 0)
                        continue;

                    // BFS from unassigned row i
                    var parent = new int[n];
                    for (int k = 0; k < n; k++) parent[k] = -1;

                    var queue = new Queue<int>();
                    queue.Enqueue(i);
                    bool found = false;
                    int endCol = -1;

                    while (queue.Count > 0 && !found)
                    {
                        int row = queue.Dequeue();
                        for (int j = 0; j < n; j++)
                        {
                            if (C[row, j] < 1e-6f && parent[j] == -1)
                            {
                                parent[j] = row;
                                if (colAssignment[j] == -1)
                                {
                                    endCol = j;
                                    found = true;
                                    break;
                                }
                                queue.Enqueue(colAssignment[j]);
                            }
                        }
                    }

                    if (found)
                    {
                        // Augment path
                        int col = endCol;
                        while (col >= 0)
                        {
                            int row = parent[col];
                            int prevCol = rowAssignment[row];
                            rowAssignment[row] = col;
                            colAssignment[col] = row;
                            col = prevCol;
                        }
                        improved = true;
                    }
                }

                if (!improved)
                {
                    // Find minimum uncovered value and adjust matrix
                    coveredRow = new bool[n];
                    coveredCol = new bool[n];

                    // Cover columns with assignments
                    for (int j = 0; j < n; j++)
                        if (colAssignment[j] >= 0)
                            coveredCol[j] = true;

                    // Cover rows without assignments
                    for (int i = 0; i < n; i++)
                        if (rowAssignment[i] == -1)
                            coveredRow[i] = true;

                    // Propagate: if a covered row has a zero in a covered column,
                    // uncover that column and cover the row assigned to it
                    bool changed = true;
                    while (changed)
                    {
                        changed = false;
                        for (int i = 0; i < n; i++)
                        {
                            if (!coveredRow[i]) continue;
                            for (int j = 0; j < n; j++)
                            {
                                if (coveredCol[j] && C[i, j] < 1e-6f)
                                {
                                    coveredCol[j] = false;
                                    int assignedRow = colAssignment[j];
                                    if (assignedRow >= 0 && !coveredRow[assignedRow])
                                    {
                                        coveredRow[assignedRow] = true;
                                        changed = true;
                                    }
                                }
                            }
                        }
                    }

                    // Invert coverage for the actual covering
                    for (int i = 0; i < n; i++) coveredRow[i] = !coveredRow[i];

                    float minUncovered = float.MaxValue;
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                            if (!coveredRow[i] && !coveredCol[j])
                                if (C[i, j] < minUncovered)
                                    minUncovered = C[i, j];

                    if (minUncovered >= float.MaxValue / 4)
                        break; // No improvement possible

                    for (int i = 0; i < n; i++)
                    {
                        for (int j = 0; j < n; j++)
                        {
                            if (!coveredRow[i] && !coveredCol[j])
                                C[i, j] -= minUncovered;
                            else if (coveredRow[i] && coveredCol[j])
                                C[i, j] += minUncovered;
                        }
                    }
                }
            }

            // Extract result (only valid rows)
            var result = new int[rows];
            for (int i = 0; i < rows; i++)
                result[i] = (rowAssignment[i] < cols) ? rowAssignment[i] : -1;

            return result;
        }
    }
}

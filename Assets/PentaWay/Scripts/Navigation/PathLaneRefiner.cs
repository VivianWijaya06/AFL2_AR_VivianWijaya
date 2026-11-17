using System;
using System.Collections.Generic;
using UnityEngine;

namespace PentaWay
{
    public static class PathLaneRefiner
    {
        public const float BoundaryTolerance = 0.06f;
        public const float OnLineTolerance = 0.05f;
        public const float MinCorridorRun = 2.0f;
        public const float StartMergeMin = 1.5f;
        public const float StartMergeSlope = 3f;
        public const float DoorCheckBackoff = 0.6f;
        public const float MinCenterLeg = 1.0f;
        const float MinLeg = 0.2f;
        const float DuplicateDistance = 0.02f;
        const float CollinearDegrees = 0.5f;

        public static bool Refine(List<Vector2> path, Vector2 door, bool appendDoor, bool alignToCorridors,
                                  Func<Vector2, Vector2, bool> isClear, bool startOnCenterLine = true)
        {
            if (path == null || path.Count < 2) return false;

            bool endsAtDoor = false;
            if (appendDoor && (path[path.Count - 1] - door).sqrMagnitude > DuplicateDistance * DuplicateDistance)
            {
                path.Add(door);
                endsAtDoor = true;
            }

            if (alignToCorridors && isClear != null)
                foreach (CorridorInfo corridor in FloorPlanData.Corridors)
                    AlignCorridor(path, corridor, endsAtDoor, isClear, startOnCenterLine);

            RemoveRedundantPoints(path);
            return endsAtDoor;
        }

        static void SnapStartToCenterLine(List<Vector2> path, CorridorInfo c, bool endsAtDoor, Func<Vector2, Vector2, bool> isClear)
        {
            if (path.Count < 2 || !Inside(c, path[0])) return;
            Vector2 start = path[0];
            if (Mathf.Abs(Lateral(c, start) - c.CenterLine) <= OnLineTolerance) return;
            Vector2 snapped = FromCorridor(c, Axial(c, start), c.CenterLine);
            bool nextIsDoor = endsAtDoor && path.Count == 2;
            if (!isClear(start, snapped) || !SegmentClear(snapped, path[1], nextIsDoor, isClear)) return;
            path[0] = snapped;
        }

        static void AlignCorridor(List<Vector2> path, CorridorInfo c, bool endsAtDoor, Func<Vector2, Vector2, bool> isClear, bool startOnCenterLine)
        {
            if (startOnCenterLine) SnapStartToCenterLine(path, c, endsAtDoor, isClear);
            InsertBoundaryCrossings(path, c);
            int i = 0;
            while (i < path.Count)
            {
                if (!Inside(c, path[i])) { i++; continue; }
                int j = i;
                while (j + 1 < path.Count && Inside(c, path[j + 1])) j++;
                i = AlignRun(path, c, i, j, endsAtDoor, isClear) + 1;
            }
        }

        static void InsertBoundaryCrossings(List<Vector2> path, CorridorInfo c)
        {
            for (int k = path.Count - 2; k >= 0; k--)
            {
                Vector2 p = path[k], q = path[k + 1];
                bool pIn = Inside(c, p), qIn = Inside(c, q);
                if (pIn && qIn) continue;
                float t0, t1;
                if (!ClipToCorridor(c, p, q, out t0, out t1)) continue;
                if (!pIn && !qIn && Mathf.Abs(Axial(c, q) - Axial(c, p)) * (t1 - t0) < MinCorridorRun) continue;
                float length = (q - p).magnitude;
                if (length < 1e-4f) continue;
                float minT = DuplicateDistance * 2f / length;
                if (!qIn && t1 > minT && t1 < 1f - minT) path.Insert(k + 1, p + (q - p) * t1);
                if (!pIn && t0 > minT && t0 < 1f - minT) path.Insert(k + 1, p + (q - p) * t0);
            }
        }

        static bool ClipToCorridor(CorridorInfo c, Vector2 p, Vector2 q, out float t0, out float t1)
        {
            float tol = BoundaryTolerance;
            t0 = 0f;
            t1 = 1f;
            Vector2 d = q - p;
            return Clip(-d.x, p.x - (c.Rect.xMin - tol), ref t0, ref t1) &&
                   Clip(d.x, (c.Rect.xMax + tol) - p.x, ref t0, ref t1) &&
                   Clip(-d.y, p.y - (c.Rect.zMin - tol), ref t0, ref t1) &&
                   Clip(d.y, (c.Rect.zMax + tol) - p.y, ref t0, ref t1) &&
                   t1 - t0 > 1e-4f;
        }

        static bool Clip(float denominator, float numerator, ref float t0, ref float t1)
        {
            if (Mathf.Abs(denominator) < 1e-7f) return numerator >= 0f;
            float t = numerator / denominator;
            if (denominator < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        static int AlignRun(List<Vector2> path, CorridorInfo c, int i, int j, bool endsAtDoor, Func<Vector2, Vector2, bool> isClear)
        {
            for (int s = i; s < j; s++)
                for (int e = j; e > s; e--)
                {
                    List<Vector2> lane = BuildLane(path, c, s, e, endsAtDoor, isClear);
                    if (lane == null) continue;
                    path.RemoveRange(s, e - s + 1);
                    path.InsertRange(s, lane);
                    return s + lane.Count - 1 + (j - e);
                }
            return j;
        }

        static List<Vector2> BuildLane(List<Vector2> path, CorridorInfo c, int i, int j, bool endsAtDoor, Func<Vector2, Vector2, bool> isClear)
        {
            Vector2 a = path[i], b = path[j];
            float axialA = Axial(c, a), axialB = Axial(c, b);
            float run = axialB - axialA;
            float center = c.CenterLine;
            bool aIsStart = i == 0;
            bool bIsDoor = endsAtDoor && j == path.Count - 1;
            float offsetA = Mathf.Abs(Lateral(c, a) - center);
            float offsetB = Mathf.Abs(Lateral(c, b) - center);
            bool needMergeIn = offsetA > OnLineTolerance;
            bool needMergeOut = offsetB > OnLineTolerance;
            bool centerStartToSideDoor = aIsStart && !needMergeIn && bIsDoor && OnSideWall(c, b);

            if (Mathf.Abs(run) < (centerStartToSideDoor ? MinLeg : MinCorridorRun)) return null;
            float dir = Mathf.Sign(run);

            for (int k = i; k < j; k++)
                if ((Axial(c, path[k + 1]) - Axial(c, path[k])) * dir < -0.3f) return null;

            float mergeIn = !needMergeIn ? 0f : aIsStart ? Mathf.Max(StartMergeMin, offsetA * StartMergeSlope) : offsetA;
            float mergeOut = !needMergeOut || (bIsDoor && OnSideWall(c, b)) ? 0f : offsetB;

            float span = Mathf.Abs(run);
            float centerLeg = span - mergeIn - mergeOut;
            if (centerLeg < MinCenterLeg && aIsStart && needMergeIn)
                mergeIn = Mathf.Max(offsetA, span - mergeOut - MinCenterLeg);
            if (!centerStartToSideDoor && span - mergeIn - mergeOut < MinCenterLeg - 1e-3f) return null;

            var lane = new List<Vector2>(4) { a };
            if (needMergeIn) lane.Add(FromCorridor(c, axialA + dir * mergeIn, center));
            if (needMergeOut) lane.Add(FromCorridor(c, axialB - dir * mergeOut, center));
            lane.Add(b);

            for (int k = 0; k + 1 < lane.Count; k++)
                if (!SegmentClear(lane[k], lane[k + 1], bIsDoor && k + 1 == lane.Count - 1, isClear))
                    return null;

            if (!aIsStart && needMergeIn && lane.Count >= 3 && SegmentClear(path[i - 1], lane[1], false, isClear))
                lane.RemoveAt(0);
            bool nextIsDoor = endsAtDoor && j + 1 == path.Count - 1;
            if (j < path.Count - 1 && needMergeOut && lane.Count >= 3 &&
                SegmentClear(lane[lane.Count - 2], path[j + 1], nextIsDoor, isClear))
                lane.RemoveAt(lane.Count - 1);
            return lane;
        }

        static bool SegmentClear(Vector2 from, Vector2 to, bool toIsDoor, Func<Vector2, Vector2, bool> isClear)
        {
            if (toIsDoor)
            {
                Vector2 back = from - to;
                float length = back.magnitude;
                if (length <= DoorCheckBackoff + 0.05f) return true;
                to += back / length * DoorCheckBackoff;
            }
            return isClear(from, to);
        }

        static void RemoveRedundantPoints(List<Vector2> path)
        {
            for (int k = path.Count - 2; k >= 1; k--)
                if ((path[k] - path[k - 1]).sqrMagnitude < DuplicateDistance * DuplicateDistance)
                    path.RemoveAt(k);
            if (path.Count > 2 && (path[path.Count - 1] - path[path.Count - 2]).sqrMagnitude < DuplicateDistance * DuplicateDistance)
                path.RemoveAt(path.Count - 2);

            for (int k = path.Count - 2; k >= 1; k--)
            {
                Vector2 before = path[k] - path[k - 1];
                Vector2 after = path[k + 1] - path[k];
                if (before.sqrMagnitude < 1e-6f || after.sqrMagnitude < 1e-6f) continue;
                float angle = Mathf.Abs(Mathf.DeltaAngle(FloorPlanData.PlanBearing(before), FloorPlanData.PlanBearing(after)));
                if (angle < CollinearDegrees) path.RemoveAt(k);
            }
        }

        static float Axial(CorridorInfo c, Vector2 p) => c.AlongZ ? p.y : p.x;
        static float Lateral(CorridorInfo c, Vector2 p) => c.AlongZ ? p.x : p.y;
        static Vector2 FromCorridor(CorridorInfo c, float axial, float lateral) => c.AlongZ ? new Vector2(lateral, axial) : new Vector2(axial, lateral);

        static bool Inside(CorridorInfo c, Vector2 p)
        {
            float t = BoundaryTolerance;
            return p.x >= c.Rect.xMin - t && p.x <= c.Rect.xMax + t && p.y >= c.Rect.zMin - t && p.y <= c.Rect.zMax + t;
        }

        static bool OnSideWall(CorridorInfo c, Vector2 p)
        {
            float lateral = Lateral(c, p);
            return Mathf.Abs(lateral - c.LateralMin) <= BoundaryTolerance || Mathf.Abs(lateral - c.LateralMax) <= BoundaryTolerance;
        }
    }
}

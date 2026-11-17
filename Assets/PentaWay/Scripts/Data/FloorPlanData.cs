using UnityEngine;

namespace PentaWay
{
    public enum Facing {North, East, South, West}

    [System.Serializable]
    public struct RectXZ
    {
        public float xMin, zMin, xMax, zMax;

        public RectXZ(float xMin, float zMin, float xMax, float zMax)
        {
            this.xMin = xMin; this.zMin = zMin; this.xMax = xMax; this.zMax = zMax;
        }

        public Vector2 Center => new Vector2((xMin + xMax) * 0.5f, (zMin + zMax) * 0.5f);
        public Vector2 Size => new Vector2(xMax - xMin, zMax - zMin);
    }

    public sealed class RoomInfo
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Subtitle;
        public readonly RectXZ Rect;
        public readonly Vector2 Door;
        public readonly Facing WalkwaySide;
        public readonly float DoorWidth;
        public readonly bool IsDestination;

        public RoomInfo(string id, string displayName, string subtitle, RectXZ rect,
                        Vector2 door, Facing walkwaySide, float doorWidth, bool isDestination)
        {
            Id = id; DisplayName = displayName; Subtitle = subtitle; Rect = rect;
            Door = door; WalkwaySide = walkwaySide; DoorWidth = doorWidth; IsDestination = isDestination;
        }
    }

    public sealed class WallInfo
    {
        public readonly Vector2 A, B;
        public WallInfo(float ax, float az, float bx, float bz) { A = new Vector2(ax, az); B = new Vector2(bx, bz); }
    }

    public sealed class BlockInfo
    {
        public readonly string Name;
        public readonly RectXZ Rect;
        public BlockInfo(string name, float x0, float z0, float x1, float z1) { Name = name; Rect = new RectXZ(x0, z0, x1, z1); }
    }

    public sealed class OpeningInfo
    {
        public readonly Vector2 Center;
        public readonly float Width;
        public readonly bool HasLintel;
        public OpeningInfo(float x, float z, float width, bool hasLintel) { Center = new Vector2(x, z); Width = width; HasLintel = hasLintel; }
    }

    public sealed class CorridorInfo
    {
        public readonly string Name;
        public readonly RectXZ Rect;
        public readonly bool AlongZ;

        public CorridorInfo(string name, float x0, float z0, float x1, float z1, bool alongZ)
        {
            Name = name; Rect = new RectXZ(x0, z0, x1, z1); AlongZ = alongZ;
        }

        public float CenterLine => AlongZ ? (Rect.xMin + Rect.xMax) * 0.5f : (Rect.zMin + Rect.zMax) * 0.5f;
        public float AxialMin => AlongZ ? Rect.zMin : Rect.xMin;
        public float AxialMax => AlongZ ? Rect.zMax : Rect.xMax;
        public float LateralMin => AlongZ ? Rect.xMin : Rect.zMin;
        public float LateralMax => AlongZ ? Rect.xMax : Rect.zMax;
    }

    public sealed class ReferencePoint
    {
        public readonly string Name;
        public readonly Vector2 WallPoint;
        public readonly float CenterHeight;
        public readonly Facing Facing;

        public ReferencePoint(string name, float x, float z, float centerHeight, Facing facing)
        {
            Name = name; WallPoint = new Vector2(x, z); CenterHeight = centerHeight; Facing = facing;
        }
    }

    public static class FloorPlanData
    {
        public const string FloorName = "Lantai 5 IMT Floor";
        public const float PlanNorthTrueBearing = 4f;

        public const float WallHeight = 3.0f;
        public const float WallThickness = 0.15f;
        public const float DoorHeight = 2.1f;
        public const float DefaultDoorWidth = 0.9f;
        public const float FloorThickness = 0.1f;
        public const float ApproachOffset = 0.5f;
        public const float LabelHeight = 2.35f;

        public static readonly RectXZ[] FloorSlabs =
        {
            new RectXZ( 0.0f, 16.4f, 22.5f, 56.4f),
            new RectXZ( 1.3f,  0.0f, 20.9f, 16.4f),
        };

        public static readonly WallInfo[] Walls =
        {
            new WallInfo( 0.0f, 56.4f, 22.5f, 56.4f),
            new WallInfo(22.5f, 16.4f, 22.5f, 56.4f),
            new WallInfo( 0.0f, 16.4f,  0.0f, 56.4f),
            new WallInfo(12.5f, 16.4f, 22.5f, 16.4f),
            new WallInfo( 0.0f, 16.4f, 10.0f, 16.4f),

            new WallInfo(12.5f, 24.4f, 12.5f, 56.4f),
            new WallInfo(10.0f, 24.4f, 10.0f, 56.4f),

            new WallInfo(12.5f, 48.4f, 22.5f, 48.4f),
            new WallInfo(12.5f, 40.4f, 22.5f, 40.4f),
            new WallInfo(12.5f, 32.4f, 22.5f, 32.4f),
            new WallInfo(12.5f, 28.4f, 22.5f, 28.4f),
            new WallInfo(12.5f, 24.4f, 22.5f, 24.4f),

            new WallInfo( 0.0f, 48.4f, 10.0f, 48.4f),
            new WallInfo( 0.0f, 40.4f, 10.0f, 40.4f),
            new WallInfo( 0.0f, 32.4f, 10.0f, 32.4f),
            new WallInfo( 0.0f, 24.4f, 10.0f, 24.4f),
            new WallInfo(10.0f, 16.4f, 10.0f, 24.4f),

            new WallInfo(20.9f,  0.0f, 20.9f, 16.4f),
            new WallInfo( 1.3f,  0.0f, 20.9f,  0.0f),
            new WallInfo( 1.3f,  0.0f,  1.3f, 16.4f),
        };

        public static readonly BlockInfo[] Blocks =
        {
            new BlockInfo("Lift_South",    12.5f, 13.6f, 20.9f, 16.4f),
            new BlockInfo("Lift_North",    12.5f,  3.7f, 20.9f,  6.8f),
            new BlockInfo("Tangga",        12.5f,  0.0f, 20.9f,  3.7f),
            new BlockInfo("Gudang",         1.3f,  0.0f, 10.0f,  3.2f),
            new BlockInfo("Toilet_Pria",    1.3f,  3.2f, 10.0f,  5.8f),
            new BlockInfo("Shaft",          8.9f,  5.8f, 10.0f, 11.6f),
            new BlockInfo("Toilet_Wanita",  1.3f,  5.8f,  7.5f, 11.6f),
            new BlockInfo("Service_Row",    1.3f, 13.6f, 10.0f, 16.4f),
        };

        public static readonly RoomInfo[] Rooms =
        {
            Room("A501", "A501", "Ruangan", 0.0f, 16.4f, 10.0f, 24.4f, 10.0f, 17.8f, Facing.East),
            Room("A502", "A502", "Ruang Dosen", 0.0f, 24.4f, 10.0f, 32.4f, 10.0f, 25.5f, Facing.East),
            Room("A503", "A503", "Ruang Kelas", 0.0f, 32.4f, 10.0f, 40.4f, 10.0f, 34.0f, Facing.East),
            Room("A504", "A504", "Ruang Kelas", 0.0f, 40.4f, 10.0f, 48.4f, 10.0f, 41.8f, Facing.East),
            Room("A505", "A505", "Ruang Kelas", 0.0f, 48.4f, 10.0f, 56.4f, 10.0f, 50.0f, Facing.East),
            Room("A506", "A506", "Laboratorium Fotografi", 12.5f, 48.4f, 22.5f, 56.4f, 12.5f, 53.4f, Facing.West),
            Room("A507", "A507", "Ruang Kelas", 12.5f, 40.4f, 22.5f, 48.4f, 12.5f, 47.3f, Facing.West),
            Room("A508", "A508", "Ruang Kelas", 12.5f, 32.4f, 22.5f, 40.4f, 12.5f, 39.3f, Facing.West),
            Room("A509", "A509", "Ruangan", 12.5f, 28.4f, 22.5f, 32.4f, 12.5f, 30.0f, Facing.West),
            Room("A510", "A510", "Ruangan", 12.5f, 24.4f, 22.5f, 28.4f, 12.5f, 25.9f, Facing.West),
            Room("LOBBY", "Lobby Lift", "Lift", 12.5f,  6.8f, 20.9f, 13.6f, 12.5f, 10.2f, Facing.West, true, 6.8f),
            Room("TOILET_PRIA", "Toilet Pria", "Toilet", 1.3f, 3.2f, 10.0f, 5.8f, 8.2f, 5.8f, Facing.North, true),
            Room("TOILET_WANITA", "Toilet Wanita", "Toilet", 1.3f, 5.8f, 7.5f, 11.6f, 2.2f, 11.6f, Facing.North, true),
            Room("TANGGA", "Tangga", "Tangga", 12.5f, 0.0f, 20.9f, 3.7f, 12.5f, 0.8f, Facing.West, true),
            Room("GUDANG", "Gudang", "Gudang", 1.3f, 0.0f, 10.0f, 3.2f, 10.0f, 0.8f, Facing.East, true),
            Room("TANGGA_DARURAT", "Tangga Darurat", "Tangga darurat", 10.0f, 56.4f, 12.5f, 59.4f, 11.25f, 56.4f, Facing.South, true),
        };

        public static readonly OpeningInfo[] ExtraOpenings = new OpeningInfo[0];

        public static readonly CorridorInfo[] Corridors =
        {
            new CorridorInfo("Koridor_Utama", 10.0f, 0.0f, 12.5f, 56.4f, true),
        };

        public static readonly ReferencePoint[] ReferencePoints =
        {
            new ReferencePoint("P1", 13.9f, 6.8f, 2.80f, Facing.North),
            new ReferencePoint("P2", 13.9f, 10.6f, 2.80f, Facing.South),
            new ReferencePoint("S1", 10.0f, 15.0f, 2.40f, Facing.East),
        };

        public static readonly Vector2 TestStartPosition = new Vector2(11.25f, 36.6f);
        public static readonly Facing TestStartFacing = Facing.North;

        static RoomInfo Room(string id, string name, string subtitle,
                             float x0, float z0, float x1, float z1,
                             float doorX, float doorZ, Facing walkwaySide,
                             bool isDestination = true, float doorWidth = DefaultDoorWidth)
        {
            return new RoomInfo(id, name, subtitle, new RectXZ(x0, z0, x1, z1),
                                new Vector2(doorX, doorZ), walkwaySide, doorWidth, isDestination);
        }

        public static ReferencePoint FindReferencePoint(string name)
        {
            foreach (ReferencePoint point in ReferencePoints)
                if (point.Name == name) return point;
            return null;
        }

        public static Vector2 ToVector(Facing f)
        {
            switch (f)
            {
                case Facing.North: return new Vector2(0f, 1f);
                case Facing.East: return new Vector2(1f, 0f);
                case Facing.South: return new Vector2(0f, -1f);
                default: return new Vector2(-1f, 0f);
            }
        }

        public static float ToYaw(Facing f) => (int)f * 90f;

        public static float PlanBearing(Vector2 direction) => Mathf.Repeat(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 360f);

        public static Vector3 ToLocal(Vector2 plan, float height = 0f) => new Vector3(plan.x, height, plan.y);

        public static Vector2 ApproachPoint(RoomInfo room) => room.Door + ToVector(room.WalkwaySide) * ApproachOffset;

        public static Vector3 ReferenceSurfacePoint(ReferencePoint point)
        {
            Vector2 p = point.WallPoint + ToVector(point.Facing) * (WallThickness * 0.5f);
            return ToLocal(p, point.CenterHeight);
        }
    }
}
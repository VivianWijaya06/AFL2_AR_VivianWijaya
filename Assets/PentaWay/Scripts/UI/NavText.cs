using UnityEngine;

namespace PentaWay
{
    public static class NavText
    {
        public const string WelcomeButton = "Mulai";

        public const string ChooseDestinationTitle = "Mau ke mana hari ini?";
        public const string ChooseDestinationHint = "Pilih ruangan, aku antar ya";
        public const string CalibrateTitle = "Tentukan posisi awal";
        public const string CompassHelp = "Pilih titik awal berdasarkan posisi mu sekarang";
        public const string MapHelp = "Pilih titik awal, lalu tekan mulai dari sini.";

        public const string ModeCompass = "Kompas";
        public const string ModeMap = "Peta";
        public const string CompassTip = "Pegang HP tegak, kamera menghadap arah jalan.";
        public const string NoSensorsMap = "Sensor gerak HP tidak tersedia. Memakai mode peta.";

        public const string SearchPlaceholder = "Cari ruangan...";
        public const string NoResults = "Ruangan tidak ditemukan.";
        public const string ArrivedTitle = "Kamu sudah sampai!";
        public const string Close = "Tutup";
        public const string OtherDestination = "Tujuan lain";
        public const string ImHere = "Saya di sini";
        public const string StartHere = "Mulai dari sini";
        public const string TestMode = "Mode uji";
        public const string Back = "Kembali";

        public const string ChooseStartFirst = "Pilih titik awal dulu";
        public const string TestModeOn = "Mode uji aktif: kamu dianggap berdiri di tengah lorong.";
        public const string OffMap = "Posisimu di luar peta. Atur ulang posisi.";
        public const string NoPath = "Rute tidak ditemukan. Coba atur ulang posisi.";

        public const string NorthLabel = "Utara";
        public const string NorthShort = "U";
        public const string DevToolsOn = "Alat pengembang ditampilkan.";
        public const string DevToolsOff = "Alat pengembang disembunyikan.";

        public const string Straight = "Lurus";
        public const string SlightLeft = "Serong kiri";
        public const string SlightRight = "Serong kanan";
        public const string Left = "Belok kiri";
        public const string Right = "Belok kanan";
        public const string UTurn = "Putar balik";
        public const string FollowArrows = "ikuti garis di lantai";
        public const string LookDownToPath = "Arahkan HP sedikit ke bawah";
        public const string LookUpToPath = "Arahkan HP sedikit ke atas";
        public const string LookLeftToPath = "Arahkan HP ke kiri";
        public const string LookRightToPath = "Arahkan HP ke kanan";
        public const string TurnLeftToPath = "Putar ke kiri untuk melihat garis";
        public const string TurnRightToPath = "Putar ke kanan untuk melihat garis";
        public const string TurnAroundToPath = "Balik badan, garis ada di belakang";
        public const string PathAhead = "Garis ada di depan";

        public const string DestinationTag = "Tujuan";
        public const string ArrivedTag = "Sudah sampai";
        public const string MinutesCaption = "menit";

        static readonly string[] Cardinals = { "Utara", "Timur Laut", "Timur", "Tenggara", "Selatan", "Barat Daya", "Barat", "Barat Laut" };

        public static string ModeName(PositioningMode mode) => mode == PositioningMode.Map ? ModeMap : ModeCompass;
        public static string StickerStart(string name) => name;
        public static string DoorStart(string room) => "Pintu " + room;
        public static string StartSelected(string name) => "Titik awal: " + name + ".";
        public static string Calibrated(string startPoint) => "Posisi diatur di " + startPoint + ".";
        public static string DestinationChosen(string room) => "Tujuan: " + room + ". Sekarang tentukan posisi awalmu.";
        public static string NavigationStarted(string room) => "Navigasi ke " + room + " dimulai, ikuti garis di lantai.";

        public static string TurnName(TurnKind turn)
        {
            switch (turn)
            {
                case TurnKind.SlightLeft: return SlightLeft;
                case TurnKind.SlightRight: return SlightRight;
                case TurnKind.Left: return Left;
                case TurnKind.Right: return Right;
                case TurnKind.UTurn: return UTurn;
                default: return Straight;
            }
        }

        public static string TurnTo(TurnKind turn, string room) => TurnName(turn) + " ke " + room;
        public static string TurnIn(TurnKind turn, float meters) => TurnName(turn) + " dalam " + Meters(meters);
        public static string StraightTo(string room) => "Lurus ke " + room;
        public static string Towards(string room) => "menuju " + room;
        public static string ThenTurn(TurnKind turn) => "lalu " + TurnName(turn).ToLowerInvariant();
        public static string RoomOnSide(string room, int side) => room + (side < 0 ? " di sebelah kiri" : side > 0 ? " di sebelah kanan" : " di depanmu");
        public static string Arrived(string room) => "Kamu sudah sampai di " + room;
        public static string DistanceLabel(float meters) => "Jarak: " + Meters(meters);
        public static string Meters(float meters) => meters < 1f ? "< 1 m" : Mathf.RoundToInt(meters) + " m";
        public static string ArrivalClock(float seconds) => "tiba " + System.DateTime.Now.AddSeconds(seconds).ToString("HH:mm");

        public static string Cardinal(float trueHeading)
        {
            int index = Mathf.RoundToInt(Mathf.Repeat(trueHeading, 360f) / 45f) % 8;
            return Cardinals[index];
        }
    }
}
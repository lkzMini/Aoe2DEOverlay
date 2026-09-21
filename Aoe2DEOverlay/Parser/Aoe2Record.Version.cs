using System.IO;

namespace ReadAoe2Recrod;

public partial class Aoe2Record
{
    public string GameVersion = "";
    public double SaveVersion;
    public bool IsDE;
    public double Version;

    private void ReadVersion(BinaryReader reader)
    {
        GameVersion = CString(reader);
        SaveVersion = Math.Round(reader.ReadSingle(), 2);
        IsDE = GameVersion == "VER 9.4";
        if (!IsDE) return;

        var data = ((MemoryStream)reader.BaseStream).ToArray();
        for (var offset = 12; offset <= Math.Min(32, data.Length - 4); offset += 4)
        {
            var candidate = BitConverter.ToUInt32(data, offset);
            if (candidate is >= 1_577_836_800 and <= 2_051_222_400)
            {
                Started = candidate;
                break;
            }
        }

        if (SaveVersion >= 12.97)
        {
            _ = reader.ReadUInt32();
            Started = reader.ReadUInt32();
            Version = reader.ReadSingle();
            var intervalVersion = reader.ReadUInt32();
            var gameOptionsVersion = reader.ReadUInt32();
            var dlcCount = reader.ReadUInt32();
            _ = ArrayUInt32(reader, (int)dlcCount);
        }
    }
}

using System.IO;
using System.Text;

namespace ReadAoe2Recrod;

public partial class Aoe2Record
{
    public uint Started;

    public void Read(BinaryReader source)
    {
        using var reader = DecompresseHeader(source);
        ReadVersion(reader);
        ReadPlayers(reader);
        IsMultiplayer = Players.Count(player => !player.IsAi) > 1;
        NumberOfPlayers = (uint)Players.Length;
    }

    public static Aoe2Record ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var record = new Aoe2Record();
        record.Read(reader);
        return record;
    }
}

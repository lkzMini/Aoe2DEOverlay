using System.IO;

namespace ReadAoe2Recrod;

public partial class Aoe2Record
{
    public RecordPlayer[] Players = Array.Empty<RecordPlayer>();

    private void ReadPlayers(BinaryReader reader)
    {
        var data = ((MemoryStream)reader.BaseStream).ToArray();
        var start = 0;
        var end = Math.Min(data.Length, start + 64 * 1024);
        var players = new Dictionary<int, RecordPlayer>();

        for (var index = start; index < end - 24; index++)
        {
            if (!TryReadDeString(data, index, end, out var firstName, out var secondStart) || string.IsNullOrWhiteSpace(firstName))
                continue;
            if (!TryReadDeString(data, secondStart, end, out var secondName, out var metadataStart) || firstName != secondName)
                continue;
            if (metadataStart + 16 > end)
                continue;

            var typeId = BitConverter.ToUInt32(data, metadataStart);
            var profileId = BitConverter.ToUInt32(data, metadataStart + 4);
            var slot = BitConverter.ToInt32(data, metadataStart + 12);
            if (typeId > 6 || slot is < 1 or > 8 || typeId == 1)
                continue;

            var preamble = FindPlayerPreamble(data, index, start);
            if (preamble < 0)
                continue;

            var color = BitConverter.ToInt32(data, preamble - 4) + 1;
            var team = data[preamble + 2];
            var civId = BitConverter.ToUInt32(data, preamble + 12);
            if (civId > 128)
                civId = 0;

            players[slot] = new RecordPlayer
            {
                Slot = slot,
                Color = color,
                Team = team,
                Name = firstName,
                Civ = ParseCiv(civId),
                ProfileId = profileId,
                TypeId = typeId
            };
        }

        Players = players.Values.OrderBy(player => player.Slot).ToArray();
        if (Players.Length == 0)
            throw new InvalidDataException("No player records were found in the replay header.");
    }

    private static bool TryReadDeString(byte[] data, int index, int end, out string value, out int next)
    {
        value = "";
        next = index;
        if (index + 4 > end || data[index] != 0x60 || data[index + 1] != 0x0A)
            return false;

        var length = BitConverter.ToUInt16(data, index + 2);
        next = index + 4 + length;
        if (next > end || length > 512)
            return false;

        value = System.Text.Encoding.UTF8.GetString(data, index + 4, length);
        return true;
    }

    private static int FindPlayerPreamble(byte[] data, int nameStart, int lowerBound)
    {
        for (var index = nameStart - 3; index >= Math.Max(lowerBound + 8, nameStart - 512); index--)
        {
            if (data[index] != 0xFF || data[index + 1] > 8 || data[index + 2] > 8)
                continue;

            var dlcId = BitConverter.ToUInt32(data, index - 8);
            var color = BitConverter.ToInt32(data, index - 4);
            if (dlcId <= 100 && color is >= -1 and <= 7)
                return index;
        }

        return -1;
    }

    public string ParseType(uint id) => id switch
    {
        0 => "Absent",
        1 => "Closed",
        2 => "Human",
        3 => "Eliminated",
        4 => "Computer",
        5 => "Cyborg",
        6 => "Spectator",
        _ => "Unknown"
    };

    public string ParseCiv(uint id) => Aoe2Mapper.ParseCiv(id);
}

public sealed class RecordPlayer
{
    public int Slot { get; init; }
    public int Color { get; init; }
    public int Team { get; init; }
    public string Name { get; init; } = "";
    public string Civ { get; init; } = "Unknown";
    public uint ProfileId { get; init; }
    public uint TypeId { get; init; }
    public bool IsAi => ProfileId == 0;
}

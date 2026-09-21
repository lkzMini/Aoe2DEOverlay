namespace Aoe2DEOverlay;

public sealed class Player
{
    public int Id { get; init; }
    public bool IsAi => Id == 0;
    public int Slot { get; init; }
    public int Color { get; init; }
    public int Team { get; init; }
    public string Name { get; init; } = "";
    public string Civ { get; init; } = "Unknown";
}

using Godot;

namespace GoDots;

public class Dot(
	float x,
	float y,
	ulong uniqueIdentifier,
	double maxLifetime,
	double dotSizeMultiplier = 1.0,
	Color? dotColor = null,
	double lifetimeMultiplier = 1.0)
{
	// A simple class to hold the minimum information needed to identify
	// a point
	public Vector2 Location = new(x, y);
	public ulong UniqueIdentifier {get; private set;} = uniqueIdentifier;

	// X and Y are lon and lat, NOT in window coordinates
	public float X
	{
		get => Location.X;
		set => Location.X = value;
	}

	public float Y
	{
		get => Location.Y;
		set => Location.Y = value;
	}

	public double Age = 0.0;
	public double MaxLifetime = maxLifetime;
	public double LifetimeMultiplier = lifetimeMultiplier;
	public Color DotColor = dotColor ?? Color.Color8(255, 255, 255, 255);
	public double DotSizeMultiplier = dotSizeMultiplier;
}

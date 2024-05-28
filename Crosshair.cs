using Godot;
using System;

public partial class Crosshair : Sprite2D
{
	private double RotationRate;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		RotationRate = Double.Pi * 45.0 / 180.0; // Radians per second
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		float newRotation = Rotation + (float)(delta * RotationRate);
		while (newRotation > (2.0f * Single.Pi))
		{
			newRotation -= 2.0f * Single.Pi;
		}
		Rotation = newRotation;
	}
}

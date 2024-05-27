﻿using System;
using System.Collections.Generic;
using Godot;

namespace GoDots;

public class IdleSimulator
{
	private readonly LinkedList<Dot> _pointList;
	private readonly RandomNumberGenerator _random;
	private ulong _nextUniqueIdentifier;

	public IdleSimulator()
	{
		_random = new RandomNumberGenerator();
		_random.Randomize();
		_nextUniqueIdentifier = 1;
		_pointList = new LinkedList<Dot>();
	}

	public void ClearList()
	{
		_pointList.Clear();
	}
	
	public void AdvanceSimulation(double timeStep)
	{
		// Rate of new point creation, in points per hour
		double newPointRate = 1.0 / 3600.0;
		// Rate of point destruction, in points per hour
		double pointLossRate = newPointRate;
		// We have a rate at which points are created and a rate at which they are lost
		// Interface to external code to grab the updated list of point locations and properties
		// Take the existing points and move them East at 200 kph
		const float uSpeed = 200.0f * 1000.0f / 3600.0f; // Change from kph to m/s
		foreach (Dot point in _pointList)
		{
			float lon = point.X;
			float lat = point.Y;
			// Don't change latitude, but do increase longitude
			double localCircumference = 6378.0e3 * Math.PI * 2.0 * Math.Cos(Math.PI * lat / 180.0);
			float newLon = (float)(lon + timeStep * uSpeed * 360.0f / localCircumference);
			while (newLon >= 180.0f)
			{
				newLon -= 360.0f;
			}

			while (newLon < -180.0f)
			{
				newLon += 360.0f;
			}
			point.X = newLon;
			point.Age += timeStep;
		}
		// Create new points
		float probability = _random.Randf();
		int nNew = 0;
		double factorMult = 1.0;
		double lambda = timeStep * newPointRate; // Expected number of events in the given interval
		while (nNew < 5 && Math.Pow(lambda,nNew) * Math.Exp(-1.0 * lambda)/factorMult < probability)
		{
			nNew++;
			factorMult *= nNew;
		}
		GenerateRandomPoints(nNew);
		// Cull points by age
		LinkedListNode<Dot>? nextNode = null;
		LinkedListNode<Dot>? node = _pointList.First;
		while (node != null)
		{
			nextNode = node.Next;
			if (node.Value.Age > node.Value.MaxLifetime)
			{
				_pointList.Remove(node);
			}
			node = nextNode;
		}
	}

	public IEnumerable<Dot> GetPointData()
	{
		return _pointList;
	}
	
	public void GenerateRandomPoints(int nLocations)
	{
		for (int i=0; i < nLocations; i++)
		{
			float lon = _random.Randf() * 360.0f - 180.0f;
			float lat = _random.Randf() * 180.0f - 90.0f;
			AddPoint(lon, lat);
		}
	}

	public void CreateInteractivePoint(float lon, float lat)
	{
		AddPoint(lon, lat);
	}
	
	private void AddPoint(float lon, float lat)
	{
		// Lifetime of 1-24 hours for all points
		double maxLifetime = 3600.0 * (24.0 * _random.Randf() + 1.0);
		_pointList.AddLast(new Dot(lon,lat,_nextUniqueIdentifier,maxLifetime));
		_nextUniqueIdentifier++;
	}
}
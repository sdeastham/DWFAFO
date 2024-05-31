using System;
using System.Collections.Generic;

namespace GoDots;

public interface ISimulator
{
    public void Advance(double dt);
    public IEnumerable<Dot> GetPointData();
    public DateTime GetCurrentTime();
    public void FlyFlight(float startLon, float startLat, float endLon, float endLat);
}
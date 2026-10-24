using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace GeoTrack.Web.Data;

/// <summary>SRID 4326 (WGS84) nokta üretimi. Sıra: X = boylam, Y = enlem.</summary>
public static class GeoFactory
{
    public const int Srid = 4326;

    private static readonly GeometryFactory Factory =
        NtsGeometryServices.Instance.CreateGeometryFactory(srid: Srid);

    public static Point CreatePoint(double latitude, double longitude) =>
        Factory.CreatePoint(new Coordinate(longitude, latitude));
}

using System.Globalization;
using System.Text;
using System.Xml;
using BDP.App.Models;

namespace BDP.App.Services;

public sealed class GpxSerializer : IGpxSerializer
{
    public string Serialize(IReadOnlyList<TrackPoint> points, DateTimeOffset startTime)
    {
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false
        }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("gpx", "http://www.topografix.com/GPX/1/1");
            writer.WriteAttributeString("version", "1.1");
            writer.WriteAttributeString("creator", "BikeDataProject");

            writer.WriteStartElement("trk");
            writer.WriteElementString("name", $"Ride {startTime:yyyy-MM-ddTHH:mm:ssZ}");
            writer.WriteElementString("type", "cycling");

            writer.WriteStartElement("trkseg");

            foreach (var pt in points)
            {
                writer.WriteStartElement("trkpt");
                writer.WriteAttributeString("lat", pt.Latitude.ToString("F7", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("lon", pt.Longitude.ToString("F7", CultureInfo.InvariantCulture));

                if (pt.Elevation.HasValue)
                {
                    writer.WriteElementString("ele", pt.Elevation.Value.ToString("F1", CultureInfo.InvariantCulture));
                }

                writer.WriteElementString("time", pt.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

                writer.WriteEndElement(); // trkpt
            }

            writer.WriteEndElement(); // trkseg
            writer.WriteEndElement(); // trk
            writer.WriteEndElement(); // gpx
            writer.WriteEndDocument();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

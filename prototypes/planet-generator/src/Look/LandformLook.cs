using System;
using System.Threading.Tasks;
using Planets.Gen;

namespace Planets.Look;

/// <summary>
/// A key colour per landform, for the landform map (the L key on the globe and the flat map,
/// and the landform atlas). Related landforms share a hue family: greens for open lowland,
/// browns and greys for the uplands, oranges for the deserts, blues and teals for the wet and
/// the cold, reds for the volcanic, violets for the alien.
/// </summary>
public static class LandformLook
{
    public static Rgb Color(Landform l) => Colors[(int)l];

    static readonly Rgb[] Colors = Build();

    static Rgb[] Build()
    {
        var c = new Rgb[LandformInfo.Count];
        void Set(Landform l, string hex) => c[(int)l] = Rgb.Hex(hex);
        Set(Landform.Plains, "8fd14f"); Set(Landform.Steppe, "d8d36a"); Set(Landform.Hills, "4f9a3c");
        Set(Landform.Mountains, "8a7f78"); Set(Landform.Plateau, "b88a5a"); Set(Landform.Mesas, "d9773a");
        Set(Landform.Canyonlands, "a8432a"); Set(Landform.RiverValleys, "2fb59a"); Set(Landform.DuneSea, "f6c25a");
        Set(Landform.RockyDesert, "c9b296"); Set(Landform.Badlands, "e0915c"); Set(Landform.Wetlands, "3c7d6a");
        Set(Landform.LakeDistrict, "4aa0d8"); Set(Landform.Delta, "18d0d0"); Set(Landform.IcePlain, "eef6ff");
        Set(Landform.Fjords, "5a7fb0"); Set(Landform.CoastalCliffs, "f0f0c8"); Set(Landform.Archipelago, "ffe08a");
        Set(Landform.VolcanicField, "d8322a"); Set(Landform.BasaltPlains, "4a3a40"); Set(Landform.Karst, "a8d8a0");
        Set(Landform.ShardFields, "b45ee6"); Set(Landform.DomeFields, "e8682c"); Set(Landform.Ringlands, "7a5cd0");
        return c;
    }

    /// <summary>The landform map as RGBA8 for the globe: alpha 255 over water, as the colour map has it, so seas stay glossy.</summary>
    public static byte[] Albedo(WorldMap map, int width)
    {
        byte[] rgb = Map(map, width);
        int height = width / 2;
        var rgba = new byte[width * height * 4];
        double sx = map.WidthMeters / width, sy = map.HeightMeters / height;
        for (int py = 0; py < height; py++)
        for (int px = 0; px < width; px++)
        {
            int p = py * width + px;
            rgba[p * 4] = rgb[p * 3];
            rgba[p * 4 + 1] = rgb[p * 3 + 1];
            rgba[p * 4 + 2] = rgb[p * 3 + 2];
            rgba[p * 4 + 3] = map.Sample(map.Elevation, (px + 0.5) * sx, (py + 0.5) * sy) < 0f ? (byte)255 : (byte)0;
        }
        return rgba;
    }

    /// <summary>
    /// The planet's landform map at the given width (height is half): each point coloured by the
    /// blend of landforms there, shaded by relief, rivers drawn, seas dark. RGB8.
    /// </summary>
    public static byte[] Map(WorldMap map, int width)
    {
        int height = width / 2;
        var rgb = new byte[width * height * 3];
        double sx = map.WidthMeters / width, sy = map.HeightMeters / height;
        Rgb sea = Rgb.Hex("152a44"), shallow = Rgb.Hex("23466a");
        Parallel.For(0, height, py =>
        {
            for (int px = 0; px < width; px++)
            {
                double x = (px + 0.5) * sx, y = (py + 0.5) * sy;
                float e = map.Sample(map.Elevation, x, y);
                Rgb col;
                if (e < 0f) col = Rgb.Lerp(shallow, sea, Mathx.Smooth(0f, 60f, -e));
                else
                {
                    LandformBlend blend = map.LandformsAt(x, y);
                    col = default;
                    for (int i = 0; i < blend.Count; i++) col += Color(blend.LandformAt(i)) * blend.WeightAt(i);
                    // Relief shading from the north-west.
                    float d = (float)map.CellSize;
                    float gx = Math.Max(0f, map.Sample(map.Elevation, x + d, y)) - Math.Max(0f, map.Sample(map.Elevation, x - d, y));
                    float gy = Math.Max(0f, map.Sample(map.Elevation, x, y + d)) - Math.Max(0f, map.Sample(map.Elevation, x, y - d));
                    float shade = Math.Clamp(1f + (gx + gy) * -0.012f, 0.55f, 1.3f);
                    col *= shade;
                }
                int o = (py * width + px) * 3;
                rgb[o] = Rgb.Byte(col.R);
                rgb[o + 1] = Rgb.Byte(col.G);
                rgb[o + 2] = Rgb.Byte(col.B);
            }
        });
        return rgb;
    }
}

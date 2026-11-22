using System.Collections.Generic;
using PlanetSurface.World;
using PoiGenerator.Gen;

namespace PlanetSurface.Infra;

public partial class InfraLayer
{
    /// <summary>What the flavour ruins' worlds hung on them: the ruins themselves are places (<see cref="FlavourRuin.Site"/>), drawn and stood up by the places' layers.</summary>
    public static IEnumerable<Piece> RuinPieces(PlanetWorld world)
    {
        foreach (FlavourRuin ruin in world.Infra.Ruins)
        {
            var (cx, cz) = ruin.Center;
            yield return new RuinPiece { Ruin = ruin, X = cx, Z = cz };
        }
    }

    /// <summary>A ruin's front as its world dressed it: marks by the door, a shop's awning and sign, from the place as built.</summary>
    sealed class RuinPiece : Piece
    {
        public FlavourRuin Ruin;

        public override void Emit(TileBuilder b)
        {
            PoiInstance inst = Ruin.Site.Instance;
            b.DressHouse(Ruin, inst, b.Local(Ruin.OriginX, Ruin.OriginY, Ruin.OriginZ));
        }
    }
}

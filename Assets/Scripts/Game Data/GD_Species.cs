using System.Collections.Generic;

// a species of lifeform of a planet - the original's kernel made them per planet in code (dir.txt: VITA-OV, HP-OV and LP-OV), which is not in the sources, so they are
// made here from the planet's seed and its bio density with the species record of the 1984 design notes (planet.txt, lf-words.txt): body plan, niche, size,
// hit points, aggression, intelligence, stun points, movement and composition - the owner's ruling of 2026-10-11 (roadmap D3: rebuild from the notes); the
// ranges and the kinds are the port's, where the notes give none
public class GD_Species
{
	// the notes' body plans (the icons of planet.txt, 8 to 24), animals first
	public enum BodyPlan
	{
		Snakelike,
		BilateralNoLegs,
		BilateralOneLeg,
		BilateralTwoLegs,
		BilateralFourLegs,
		BilateralManyLegs,
		RadialNoLegs,
		RadialFewLegs,
		RadialManyLegs,
		AmorphousNoLegs,
		AmorphousLegs,
		LeavedSmall,
		LeavedMedium,
		LeavedLarge,
		Count
	}

	// the notes' niches ("use highest niche")
	public enum Niche
	{
		Producer,
		Herbivore,
		Carnivore,
		Omnivore
	}

	// what the stunner does to it ("the lifeform may either be 1) unaffected 2) stunned for some period of time or 3) killed")
	public enum StunEffect
	{
		Unaffected,
		Stunned,
		Killed
	}

	// which species of the planet this is
	public int m_index;

	public BodyPlan m_bodyPlan;
	public Niche m_niche;
	public StunEffect m_stunEffect;

	// the size, 1 to 9 (lf-words.txt: "?BIG-ENOUGH : Is the alien's size (1-9) >= its mate's size - 2"), and its volume in tenths of a cubic meter
	public int m_size;
	public int m_volume;

	// "lifeforms may have up to 256 hit points"
	public int m_hitPoints;

	// 1 to 100 each
	public int m_aggression;
	public int m_intelligence;

	// how many stuns it takes before it is stunned
	public int m_stunPoints;

	// 0 for a lifeform that does not move ("?MOBILE : Alien's movement rate > 0"), up to 5
	public int m_movementRate;

	// whether it can take to the air ("FLY <-- ... ?CAN-FLY", "FLOAT <-- ... ?CAN-FLOAT")
	public bool m_canFly;
	public bool m_canFloat;

	// its three main elements, the first the most (chemical elements, not the game data's minerals; carbon's place in it adds to its value, planet.txt)
	public string[] m_composition;

	// its colour (planet.txt: "the particular color or icon for any given species is part of its species record")
	public float m_red;
	public float m_green;
	public float m_blue;

	// the species of each planet, made once
	static readonly Dictionary<int, List<GD_Species>> s_speciesOfPlanet = new Dictionary<int, List<GD_Species>>();

	// the species of a planet: none without life, and more the higher its bio density (1 for the lowest, up to 6)
	public static List<GD_Species> ForPlanet( GD_Planet planet )
	{
		if ( s_speciesOfPlanet.TryGetValue( planet.m_id, out var known ) )
		{
			return known;
		}

		var speciesList = new List<GD_Species>();

		if ( ( planet.m_bioDensity > 0 ) && !planet.IsGasGiant() )
		{
			// their own random numbers, so the game's do not move
			var random = new System.Random( planet.m_id * 977 + 13 );
			var count = 1 + planet.m_bioDensity / 20;

			for ( var index = 0; index < count; index++ )
			{
				speciesList.Add( Make( index, random ) );
			}
		}

		s_speciesOfPlanet[ planet.m_id ] = speciesList;

		return speciesList;
	}

	// one species, with the planet's random numbers
	static GD_Species Make( int index, System.Random random )
	{
		var species = new GD_Species { m_index = index };

		// a third of the species are plants
		var isPlant = random.Next( 3 ) == 0;

		if ( isPlant )
		{
			species.m_bodyPlan = (BodyPlan) ( (int) BodyPlan.LeavedSmall + random.Next( 3 ) );
			species.m_niche = Niche.Producer;
			species.m_aggression = 1 + random.Next( 30 );
			species.m_intelligence = 1 + random.Next( 10 );
			species.m_movementRate = 0;
			species.m_canFloat = random.Next( 10 ) == 0;
		}
		else
		{
			species.m_bodyPlan = (BodyPlan) random.Next( (int) BodyPlan.LeavedSmall );

			// half of the animals eat plants, a quarter other animals, a quarter both
			var niche = random.Next( 4 );

			species.m_niche = ( niche < 2 ) ? Niche.Herbivore : ( ( niche == 2 ) ? Niche.Carnivore : Niche.Omnivore );

			// hunters are the more aggressive
			species.m_aggression = ( species.m_niche == Niche.Herbivore ) ? 1 + random.Next( 60 ) : ( ( species.m_niche == Niche.Carnivore ) ? 30 + random.Next( 71 ) : 10 + random.Next( 81 ) );
			species.m_intelligence = 1 + random.Next( 100 );
			species.m_movementRate = 1 + random.Next( 5 );
			species.m_canFly = random.Next( 10 ) == 0;
		}

		species.m_size = isPlant ? ( 1 + ( (int) species.m_bodyPlan - (int) BodyPlan.LeavedSmall ) * 3 + random.Next( 3 ) ) : 1 + random.Next( 9 );

		// a tenth of a cubic meter for the smallest, and 0.1 more for each step, times the size again (so the largest are 8.1 cubic meters)
		species.m_volume = species.m_size * species.m_size;
		species.m_hitPoints = System.Math.Min( 256, species.m_size * 20 + random.Next( 77 ) );
		species.m_stunPoints = 10 + random.Next( 91 );

		var stunEffect = random.Next( 10 );

		species.m_stunEffect = ( stunEffect == 0 ) ? StunEffect.Unaffected : ( ( stunEffect == 9 ) ? StunEffect.Killed : StunEffect.Stunned );

		// the elements of life (planet.txt's example: "Silicon, Nitrogen, Carbon"), carbon most often among them
		var elements = new[] { "Carbon", "Nitrogen", "Silicon", "Hydrogen", "Oxygen", "Sulfur", "Phosphorus", "Calcium" };
		var composition = new List<string>();

		for ( var tries = 0; ( tries < 20 ) && ( composition.Count < 3 ); tries++ )
		{
			var name = ( random.Next( 3 ) == 0 ) ? "Carbon" : elements[ random.Next( elements.Length ) ];

			if ( !composition.Contains( name ) )
			{
				composition.Add( name );
			}
		}

		species.m_composition = composition.ToArray();

		// any colour but the notes' greys, light green, yellow, cyan and blue-white - bright, saturated colours away from those
		var hue = new[] { 0.0f, 0.05f, 0.08f, 0.75f, 0.83f, 0.92f, 0.6f, 0.33f }[ random.Next( 8 ) ];
		var color = UnityEngine.Color.HSVToRGB( hue, 0.7f + (float) random.NextDouble() * 0.3f, 0.6f + (float) random.NextDouble() * 0.4f );

		species.m_red = color.r;
		species.m_green = color.g;
		species.m_blue = color.b;

		return species;
	}

	// a word for its aggression, as the notes' scan gives it ("Aggression analog : Rabbit")
	public string GetAggressionAnalog()
	{
		var analogs = new[] { "Mouse", "Rabbit", "Deer", "Dog", "Bear", "Wolf", "Tiger" };

		return analogs[ System.Math.Min( analogs.Length - 1, ( m_aggression - 1 ) * analogs.Length / 100 ) ];
	}

	// a word for its intelligence ("Intelligence analog : Dog")
	public string GetIntelligenceAnalog()
	{
		var analogs = new[] { "Insect", "Fish", "Bird", "Dog", "Monkey", "Dolphin", "Human" };

		return analogs[ System.Math.Min( analogs.Length - 1, ( m_intelligence - 1 ) * analogs.Length / 100 ) ];
	}

	// its niche, as the scan gives it ("Niche : Photosynthetic Herbiverore")
	public string GetNicheText()
	{
		switch ( m_niche )
		{
			case Niche.Producer:
				return "Photosynthetic producer";

			case Niche.Herbivore:
				return "Herbivore";

			case Niche.Carnivore:
				return "Carnivore";
		}

		return "Omnivore";
	}

	// its body plan, in words (the notes' icon names: "Bilateral (animal) 2 legs")
	public string GetBodyPlanText()
	{
		var texts = new[] { "Bilateral, snakelike", "Bilateral, no legs", "Bilateral, one leg", "Bilateral, two legs", "Bilateral, three or four legs", "Bilateral, many legs", "Radial, no legs", "Radial, one or two legs", "Radial, many legs", "Amorphous, no legs", "Amorphous, with legs", "Leaved plant, small", "Leaved plant, medium", "Leaved plant, large" };

		return texts[ (int) m_bodyPlan ];
	}

	// true for a plant
	public bool IsPlant()
	{
		return m_niche == Niche.Producer;
	}
}

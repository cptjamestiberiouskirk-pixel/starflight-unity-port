using System.Collections.Generic;

// a species of lifeform of a planet, as the shipped game makes them on a landing (roadmap 8.18, ShippedLifeforms: VITA-OV, HP-OV and LP-OV) - its record
// (shape, exterior, size, niche, colours, weapon, appendages, movement, aggression, intelligence, hit points, stun effect, height and look) read into the
// fields the rest of the port uses: a body plan from its icon (the port's lifeform models), its niche, size, hit points and so on
public class GD_Species
{
	// the notes' body plans (the icons of planet.txt, 8 to 24), animals first - the shipped game's 17 icons (LP-OV set.icon) map onto them
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

	// the niches (the shipped game's six: SENT-OV NICHE-SE; the two plants that eat are last, so that the first four keep their numbers)
	public enum Niche
	{
		Producer,
		Herbivore,
		Carnivore,
		Omnivore,
		HerbivorousProducer,
		CarnivorousProducer
	}

	// what the stunner does to it ("the lifeform may either be 1) unaffected 2) stunned for some period of time or 3) killed") - every lifeform of the shipped
	// game can be stunned
	public enum StunEffect
	{
		Unaffected,
		Stunned,
		Killed
	}

	// which species of the planet this is (its record number)
	public int m_index;

	public BodyPlan m_bodyPlan;
	public Niche m_niche;
	public StunEffect m_stunEffect;

	// the size, 1 to 9 (the record's size index: TINY to GARGANTUAN), and its volume in tenths of a cubic meter (the port's: the size squared)
	public int m_size;
	public int m_volume;

	// its hit points (6 to 83)
	public int m_hitPoints;

	// 1 to 100 each
	public int m_aggression;
	public int m_intelligence;

	// how many stuns it takes before it is stunned (the record's stun effect, 6 to 83)
	public int m_stunPoints;

	// 0 for a lifeform that does not move, up to 5 (from the slowest way it moves: oozing 1 to leaping 10, halved)
	public int m_movementRate;

	// whether it can take to the air (its air appendage: wings to fly, a float sac to float)
	public bool m_canFly;
	public bool m_canFloat;

	// its colour (its body's hue, tint and glow)
	public float m_red;
	public float m_green;
	public float m_blue;

	// its record (null for a species made by hand, as the probe's test animals)
	public ShippedLifeforms.Creature m_record;

	// the species of each planet, and what its generation gave (the regions, the individuals per block), made once
	static readonly Dictionary<int, List<GD_Species>> s_speciesOfPlanet = new Dictionary<int, List<GD_Species>>();
	static readonly Dictionary<int, ShippedLifeforms.Result> s_lifeOfPlanet = new Dictionary<int, ShippedLifeforms.Result>();

	// the body plan of each of the shipped game's icons (LP-OV set.icon: leaved plants large, medium and small in pairs, then snakelike, bilateral with no,
	// one, two, up to four and more legs, radial with no, few and many legs, and amorphous without and with legs)
	static readonly BodyPlan[] c_iconBodyPlans =
	{
		BodyPlan.LeavedLarge, BodyPlan.LeavedLarge, BodyPlan.LeavedMedium, BodyPlan.LeavedMedium, BodyPlan.LeavedSmall, BodyPlan.LeavedSmall, BodyPlan.Snakelike,
		BodyPlan.BilateralNoLegs, BodyPlan.BilateralOneLeg, BodyPlan.BilateralTwoLegs, BodyPlan.BilateralFourLegs, BodyPlan.BilateralManyLegs,
		BodyPlan.RadialNoLegs, BodyPlan.RadialFewLegs, BodyPlan.RadialManyLegs, BodyPlan.AmorphousNoLegs, BodyPlan.AmorphousLegs
	};

	// the species of a planet: none without life (or once a flare has sterilised it)
	public static List<GD_Species> ForPlanet( GD_Planet planet )
	{
		// a planet a flare has sterilised has none (PlayerData.GetBioDensity)
		if ( ( planet == null ) || ( PlayerData.GetBioDensityOf( planet ) <= 0 ) )
		{
			return new List<GD_Species>();
		}

		if ( s_speciesOfPlanet.TryGetValue( planet.m_id, out var known ) )
		{
			return known;
		}

		var life = ShippedLifeforms.Make( planet );
		var speciesList = new List<GD_Species>();

		for ( var index = 0; index < life.m_species.Count; index++ )
		{
			speciesList.Add( FromRecord( index, life.m_species[ index ] ) );
		}

		s_speciesOfPlanet[ planet.m_id ] = speciesList;
		s_lifeOfPlanet[ planet.m_id ] = life;

		return speciesList;
	}

	// what the generation of a planet's lifeforms gave (its regions, how crowded each is and how many of a species a block holds): an empty result without life
	public static ShippedLifeforms.Result GetLife( GD_Planet planet )
	{
		if ( ForPlanet( planet ).Count == 0 )
		{
			return new ShippedLifeforms.Result();
		}

		return s_lifeOfPlanet[ planet.m_id ];
	}

	// a species from its record
	static GD_Species FromRecord( int index, ShippedLifeforms.Creature record )
	{
		var species = new GD_Species { m_index = index, m_record = record };

		species.m_bodyPlan = c_iconBodyPlans[ UnityEngine.Mathf.Clamp( record.m_icon, 0, c_iconBodyPlans.Length - 1 ) ];
		species.m_niche = NicheOf( record.m_niche );
		species.m_size = UnityEngine.Mathf.Clamp( record.m_sizeIndex, 1, 9 );
		species.m_volume = species.m_size * species.m_size;
		species.m_hitPoints = record.m_hitPoints;
		species.m_aggression = UnityEngine.Mathf.Clamp( record.m_aggression, 1, 100 );
		species.m_intelligence = UnityEngine.Mathf.Clamp( record.m_intelligence, 1, 100 );
		species.m_stunPoints = record.m_stunEffect;
		species.m_stunEffect = StunEffect.Stunned;
		species.m_movementRate = ( record.m_slowest + 1 ) / 2;
		species.m_canFly = record.m_airMotion == "FLYING";
		species.m_canFloat = record.m_airMotion == "FLOATING";

		// a floating plant drifts (FLOAT needs ?MOVING; the rate is the port's)
		if ( species.m_canFloat && ( species.m_movementRate == 0 ) )
		{
			species.m_movementRate = 1;
		}

		var colour = GetHueColour( record.m_hue, record.m_tint, record.m_glow );

		species.m_red = colour.r;
		species.m_green = colour.g;
		species.m_blue = colour.b;

		return species;
	}

	// the niche of a record's niche number (2 producer, 4 herbivore, 6 herbivorous producer, 8 carnivore, 10 carnivorous producer, 12 omnivore)
	static Niche NicheOf( int niche )
	{
		switch ( niche & 0x0e )
		{
			case 2: return Niche.Producer;
			case 4: return Niche.Herbivore;
			case 6: return Niche.HerbivorousProducer;
			case 8: return Niche.Carnivore;
			case 10: return Niche.CarnivorousProducer;
		}

		return Niche.Omnivore;
	}

	// its niche as the shipped game's bits: 2 a producer, 4 eats plants, 8 eats animals
	public int GetNicheBits()
	{
		switch ( m_niche )
		{
			case Niche.Producer: return 2;
			case Niche.Herbivore: return 4;
			case Niche.HerbivorousProducer: return 6;
			case Niche.Carnivore: return 8;
			case Niche.CarnivorousProducer: return 10;
		}

		return 12;
	}

	// the colours of the shipped game's 27 hues (LP-OV ?COLOR), the port's
	static readonly Dictionary<string, UnityEngine.Color> c_hueColours = new Dictionary<string, UnityEngine.Color>
	{
		{ "RED", new UnityEngine.Color( 0.85f, 0.1f, 0.1f ) }, { "MARROON", new UnityEngine.Color( 0.5f, 0.06f, 0.1f ) }, { "RUST-COLORED", new UnityEngine.Color( 0.72f, 0.26f, 0.06f ) },
		{ "PINK", new UnityEngine.Color( 1.0f, 0.6f, 0.7f ) }, { "REDDISH ORANGE", new UnityEngine.Color( 0.95f, 0.3f, 0.08f ) }, { "BROWN", new UnityEngine.Color( 0.45f, 0.28f, 0.12f ) },
		{ "BEIGE", new UnityEngine.Color( 0.85f, 0.78f, 0.6f ) }, { "COPPER-COLORED", new UnityEngine.Color( 0.72f, 0.45f, 0.2f ) }, { "ORANGE", new UnityEngine.Color( 1.0f, 0.55f, 0.05f ) },
		{ "YELLOWISH ORANGE", new UnityEngine.Color( 1.0f, 0.7f, 0.1f ) }, { "YELLOW", new UnityEngine.Color( 1.0f, 0.92f, 0.1f ) }, { "LEMON YELLOW", new UnityEngine.Color( 1.0f, 0.97f, 0.4f ) },
		{ "YELLOWISH GREEN", new UnityEngine.Color( 0.7f, 0.9f, 0.2f ) }, { "BROWNISH GREEN", new UnityEngine.Color( 0.4f, 0.42f, 0.15f ) }, { "GREEN", new UnityEngine.Color( 0.15f, 0.7f, 0.15f ) },
		{ "LIME GREEN", new UnityEngine.Color( 0.5f, 1.0f, 0.2f ) }, { "BLUE-GREEN", new UnityEngine.Color( 0.05f, 0.6f, 0.55f ) }, { "TURQUOISE", new UnityEngine.Color( 0.25f, 0.85f, 0.8f ) },
		{ "BLUE", new UnityEngine.Color( 0.15f, 0.3f, 0.9f ) }, { "LAVENDAR", new UnityEngine.Color( 0.7f, 0.6f, 0.9f ) }, { "VIOLET", new UnityEngine.Color( 0.55f, 0.25f, 0.85f ) },
		{ "PURPLE", new UnityEngine.Color( 0.5f, 0.1f, 0.55f ) }, { "WHITE", new UnityEngine.Color( 0.95f, 0.95f, 0.95f ) }, { "MULTI-COLORED", new UnityEngine.Color( 0.8f, 0.5f, 0.6f ) },
		{ "GREY", new UnityEngine.Color( 0.5f, 0.5f, 0.5f ) }, { "BLACK", new UnityEngine.Color( 0.12f, 0.12f, 0.12f ) }, { "VERY BLUE", new UnityEngine.Color( 0.05f, 0.15f, 1.0f ) },
	};

	// the colour of a hue with its tint (dark, bright or light) or glow
	static UnityEngine.Color GetHueColour( string hue, string tint, string glow )
	{
		var colour = ( ( hue != null ) && c_hueColours.TryGetValue( hue, out var known ) ) ? known : new UnityEngine.Color( 0.5f, 0.5f, 0.5f );

		if ( tint == "DARK" )
		{
			colour *= 0.55f;
		}
		else if ( tint == "LIGHT" )
		{
			colour = UnityEngine.Color.Lerp( colour, UnityEngine.Color.white, 0.4f );
		}
		else if ( ( tint == "BRIGHT" ) || ( glow != null ) )
		{
			colour = UnityEngine.Color.Lerp( colour, UnityEngine.Color.white, 0.15f );
		}

		colour.a = 1.0f;

		return colour;
	}

	// the shipped game's analogs (SENT-OV ANALOGS): the first whose limit the value is within, the last for anything above
	static readonly int[] c_aggressionLimits = { 5, 10, 15, 20, 30, 31, 32, 45, 60, 75, 85, 90, 95 };
	static readonly string[] c_aggressionAnalogs = { "Daisy", "Tree", "Clam", "Goldfish", "Sheep", "Emu", "Armadillo", "Horse", "Bobcat", "Wolf", "Rhino", "Grizzly", "Piranha", "Fireshark" };
	static readonly int[] c_intelligenceLimits = { 4, 8, 12, 20, 30, 31, 32, 45, 60, 75, 90 };
	static readonly string[] c_intelligenceAnalogs = { "Sponge", "Slug", "Ant", "Lizard", "Mouse", "Emu", "Penguin", "Rabbit", "Cat", "Fox", "Monkey", "Ape" };

	static string GetAnalog( int value, int[] limits, string[] analogs )
	{
		for ( var i = 0; i < limits.Length; i++ )
		{
			if ( value <= limits[ i ] )
			{
				return analogs[ i ];
			}
		}

		return analogs[ analogs.Length - 1 ];
	}

	// a word for its aggression, as the scan gives it ("Aggression analog : Wolf")
	public string GetAggressionAnalog()
	{
		return GetAnalog( m_aggression, c_aggressionLimits, c_aggressionAnalogs );
	}

	// a word for its intelligence ("Intelligence analog : Fox")
	public string GetIntelligenceAnalog()
	{
		return GetAnalog( m_intelligence, c_intelligenceLimits, c_intelligenceAnalogs );
	}

	// its height, as the scan gives it ("Height: 2 m 13 cm", or in centimetres under a metre)
	public string GetHeightText()
	{
		var height = ( m_record != null ) ? m_record.m_heightCm : m_size * 20;

		return ( height < 100 ) ? ( height + " cm" ) : ( ( height / 100 ) + " m " + ( height % 100 ) + " cm" );
	}

	// its size in a word (for Look: the record's size word, tiny to gargantuan)
	public string GetSizeText()
	{
		var words = ShippedLifeforms.c_sizeWords;

		return words[ System.Math.Max( 1, System.Math.Min( words.Length - 1, m_size ) ) ].ToLower();
	}

	// its colour in words (for Look: its hue, with its tint or glow)
	public string GetColourText()
	{
		if ( m_record == null )
		{
			return "grey";
		}

		var hue = Spelled( m_record.m_hue ?? "GREY" );
		var shade = m_record.m_tint ?? m_record.m_glow;

		return ( ( shade != null ) ? shade.ToLower() + " " : "" ) + hue;
	}

	// a word of the shipped game in the port's spelling
	static string Spelled( string word )
	{
		switch ( word )
		{
			case "MARROON": return "maroon";
			case "LAVENDAR": return "lavender";
			case "SHINEY": return "shiny";
			case "WIREY": return "wiry";
			case "CHITENOUS": return "chitinous";
			case "BONEY": return "bony";
			case "MUCOUSY": return "mucous";
		}

		return word.ToLower();
	}

	// what it looks like, as the shipped game's description gives it (SENT-OV SAYIT): "A large thick reptilian creature with shiny green scales."
	public string GetDescription()
	{
		if ( m_record == null )
		{
			return "A " + GetSizeText() + " " + GetColourText() + " lifeform.";
		}

		var plant = IsPlant();
		var words = new List<string> { GetSizeText() };

		if ( m_record.m_proportion != null )
		{
			words.Add( m_record.m_proportion.ToLower() );
		}

		// what it resembles, or its shape (left out for a bilateral plant)
		if ( m_record.m_resembles != null )
		{
			words.Add( m_record.m_resembles.ToLower() );
		}
		else if ( ( m_record.m_shape != null ) && !( plant && ( m_record.m_shape == "BILATERAL" ) ) )
		{
			words.Add( m_record.m_shape.ToLower() );
		}

		words.Add( plant ? "lifeform" : "creature" );

		var text = string.Join( " ", words );
		var exterior = ( m_record.m_exterior ?? "SURFACE" ).ToLower();
		var covering = ( ( m_record.m_texture != null ) ? Spelled( m_record.m_texture ) + " " : "" ) + GetColourText() + " " + exterior;
		var mass = ( exterior == "scales" ) || ( exterior == "skin" ) || ( exterior == "hair" ) || ( exterior == "armor" ) || ( exterior == "fur" );

		return Article( text ) + " " + text + " with " + ( mass ? "" : Article( covering ) + " " ) + covering + ".";
	}

	// "a" or "an" before a word
	static string Article( string text )
	{
		return ( ( text.Length > 0 ) && ( "aeiou".IndexOf( char.ToLower( text[ 0 ] ) ) >= 0 ) ) ? "an" : "a";
	}

	// its niche, as the scan gives it (SENT-OV NICHE-SE)
	public string GetNicheText()
	{
		switch ( m_niche )
		{
			case Niche.Producer:
				return "Producer";

			case Niche.Herbivore:
				return "Herbivore";

			case Niche.HerbivorousProducer:
				return "Herbivorous producer";

			case Niche.Carnivore:
				return "Carnivore";

			case Niche.CarnivorousProducer:
				return "Carnivorous producer";
		}

		return "Omnivore";
	}

	// its body plan, in words (the notes' icon names: "Bilateral (animal) 2 legs")
	public string GetBodyPlanText()
	{
		var texts = new[] { "Bilateral, snakelike", "Bilateral, no legs", "Bilateral, one leg", "Bilateral, two legs", "Bilateral, three or four legs", "Bilateral, many legs", "Radial, no legs", "Radial, one or two legs", "Radial, many legs", "Amorphous, no legs", "Amorphous, with legs", "Leaved plant, small", "Leaved plant, medium", "Leaved plant, large" };

		return texts[ (int) m_bodyPlan ];
	}

	// true for a plant (a producer, also one that eats: the shipped game's niche numbers that are not a multiple of 4)
	public bool IsPlant()
	{
		return ( GetNicheBits() & 2 ) != 0;
	}

	// Arth's system in the original's units of the star map (map coordinates times 8: Arth is at 125, 100), from which ITEMS-OV ?dist measures
	const int c_arthX = 1000;
	const int c_arthY = 800;

	// its value, as the shipped game's ITEMS-OV valuate gives it: 10 for an animal or 30 for a plant (niche>val), plus a tenth of its intelligence, times its size,
	// times one more than its system's distance from Arth's in map units (?dist), a tenth of that for a dead one, at least 1 - a captured specimen holds this
	// value and a recording an eighth of it, and the Trade Depot pays half of either. The arithmetic is the original's 16 bit one: each square of the distance
	// keeps only its low 16 bits, so a system 32 map units or more from Arth on an axis is given a wrong, often smaller distance (an original quirk)
	public int GetValue( GD_Star star, bool dead )
	{
		var distance = 0;

		if ( star != null )
		{
			var dx = star.m_xCoordinate * 8 - c_arthX;
			var dy = star.m_yCoordinate * 8 - c_arthY;
			var squares = (long) ( ( dx * dx ) & 0xffff ) + ( ( dy * dy ) & 0xffff );

			distance = System.Math.Max( 1, (int) System.Math.Floor( System.Math.Sqrt( squares ) ) ) / 8;
		}

		var nicheValue = IsPlant() ? 30 : 10;
		var value = ( ( nicheValue + m_intelligence / 10 ) * m_size * ( distance + 1 ) ) & 0xffff;

		if ( dead )
		{
			value /= 10;
		}

		return System.Math.Max( 1, value );
	}
}

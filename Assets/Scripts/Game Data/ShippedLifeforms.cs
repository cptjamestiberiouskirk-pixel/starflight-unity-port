using System.Collections.Generic;
using UnityEngine;

// the lifeforms of a planet as the shipped game makes them on a landing (roadmap 8.18): VITA-OV builds the planet's regions by temperature, how crowded each is,
// which niches live there and how many classes, orders and species come of them; HP-OV gives each class its shape and exterior and each order its size, colours,
// weapon, decoration, flight and movement; LP-OV gives each species its proportion, aggression, intelligence, hit points, stun effect, height, volume and look -
// all with the kernel's own random numbers from the planet type's seed (the PLANET file's word 0x05), so a planet has the same lifeforms on every landing
public static class ShippedLifeforms
{
	// ---- the kernel's random numbers

	// slip!'s curve (the kernel's CURVE) and the mask it is tested with (FILTER: 0x2053 in the shipped STARFLT.COM, which nothing writes - the 1986 listing's
	// "128 FILTER !" is not in the shipped program, as far as the decompiled code shows)
	static readonly int[] c_curve = { 0x00, 0x08, 0x10, 0x88, 0x88, 0x89, 0x25, 0x55, 0x55, 0x57, 0x75, 0x77, 0x77, 0x7f, 0x7f, 0xff, 0xff, 0xfe, 0xf7, 0xee, 0xee, 0xae, 0xae, 0xaa, 0xa4, 0xa4, 0x91, 0x11, 0x11, 0x08, 0x10, 0x00 };
	const int c_filter = 0x2053;

	// FRND (SEED * 31421 + 6927 in 16 bits), RRND (a to b - 1), rand% (1 to 99), coin (0 or 1) and slip! (a bell about PEAK)
	public class Generator
	{
		public int m_seed;
		public int m_peak;

		public Generator( int seed )
		{
			m_seed = seed & 0xffff;
		}

		public int Next()
		{
			m_seed = ( m_seed * 31421 + 6927 ) & 0xffff;

			return m_seed;
		}

		// RRND: a + the high word of (b - a) times the next number, unsigned 16 bit (so a b below a wraps, as the kernel's does)
		public int Range( int a, int b )
		{
			return a + (int) ( ( (long) ( ( b - a ) & 0xffff ) * Next() ) >> 16 );
		}

		public int Percent()
		{
			return Range( 1, 100 );
		}

		public int Coin()
		{
			return Range( 0, 2 );
		}

		// slip!: a number of 0 to 255 until the curve takes it at the peak, scaled to lo..hi
		public int Slip( int lo, int hi )
		{
			for ( var tries = 0; tries < 100000; tries++ )
			{
				var x = Range( 0, 256 );
				var bx = x - m_peak + 128;

				if ( ( bx < 0 ) || ( bx > 255 ) )
				{
					continue;
				}

				if ( ( ( c_filter >> ( bx & 7 ) ) & c_curve[ bx >> 3 ] ) != 0 )
				{
					return lo + ( hi + 1 - lo ) * x / 256;
				}
			}

			return lo;
		}
	}

	// a 16 bit result read as signed (the kernel's / is a signed division)
	static int ToSigned16( int value )
	{
		value &= 0xffff;

		return ( value >= 0x8000 ) ? value - 0x10000 : value;
	}

	// ---- chance tables (HP-OV and LP-OV CHANCE: entries of cumulative percent and value, the first with r up to its percent wins, 100 ends the table)

	class Table
	{
		public int[] m_upTo;
		public string[] m_values;

		// "10:AMORPHOUS,20:IRREGULAR,..." ("-" for none)
		public Table( string entries )
		{
			var parts = entries.Split( ',' );

			m_upTo = new int[ parts.Length ];
			m_values = new string[ parts.Length ];

			for ( var i = 0; i < parts.Length; i++ )
			{
				var colon = parts[ i ].IndexOf( ':' );

				m_upTo[ i ] = int.Parse( parts[ i ].Substring( 0, colon ) );
				m_values[ i ] = parts[ i ].Substring( colon + 1 );

				if ( m_values[ i ] == "-" )
				{
					m_values[ i ] = null;
				}
			}
		}

		public string Pick( int r )
		{
			for ( var i = 0; i < m_upTo.Length; i++ )
			{
				if ( ( r <= m_upTo[ i ] ) || ( m_upTo[ i ] == 100 ) )
				{
					return m_values[ i ];
				}
			}

			return m_values[ m_values.Length - 1 ];
		}

		public int PickNumber( int r )
		{
			var value = Pick( r );

			return ( value == null ) ? 0 : int.Parse( value );
		}
	}

	// the kinds of lifeform: a niche whose value is not a multiple of 4 is a plant's ((kng))
	public const int c_plants = 0;
	public const int c_animals = 1;

	public static int KindOf( int niche )
	{
		return ( ( niche % 4 ) != 0 ) ? c_plants : c_animals;
	}

	// VITA-OV NICE, by the region's niche flags (producers 2, herbivores 4, carnivores 8): what each draw gives
	static readonly Dictionary<int, Table> c_niches = new Dictionary<int, Table>
	{
		{ 2, new Table( "100:2" ) },
		{ 4, new Table( "100:4" ) },
		{ 6, new Table( "45:4,90:2,100:6" ) },
		{ 12, new Table( "50:4,70:12,100:8" ) },
		{ 14, new Table( "25:2,29:6,36:10,58:4,73:12,100:8" ) },
	};

	// HP-OV SHAPES, EPIDERM (a redraw where it gives none) and APPEARANCE
	static readonly Table[] c_shapes = { new Table( "5:AMORPHOUS,10:IRREGULAR,15:SPHERICAL,45:RADIAL,100:BILATERAL" ), new Table( "10:AMORPHOUS,20:IRREGULAR,35:SPHERICAL,60:RADIAL,100:BILATERAL" ) };

	static readonly Dictionary<string, Table> c_exteriors = new Dictionary<string, Table>
	{
		{ "BILATERAL", new Table( "10:SCALES,20:EXOSKELETON,25:SKIN,30:HAIR,35:FEATHERED SURFACE,40:ARMOR,50:SURFACE,57:PATTERNED SURFACE,67:FUR,100:-" ) },
		{ "RADIAL", new Table( "10:SCALES,20:EXOSKELETON,30:FUR,35:SKIN,40:HAIR,45:ARMOR,55:SURFACE,62:PATTERNED SURFACE,100:-" ) },
		{ "SPHERICAL", new Table( "10:SCALES,10:FUR,15:HAIR,20:ARMOR,30:SURFACE,100:-" ) },
		{ "IRREGULAR", new Table( "100:SURFACE" ) },
		{ "AMORPHOUS", new Table( "100:SURFACE" ) },
	};

	static readonly Dictionary<string, Table> c_textures = new Dictionary<string, Table>
	{
		{ "SURFACE", new Table( "3:WAXY,5:DOWNY,7:FILMY,9:DULL,11:SHIVERING,13:METALLIC,15:SERRATED,17:BRISTLY,19:FUZZY,21:HAIRY,23:WARTY,25:RUBBERY,27:PLASTIC-LIKE,29:HARD,31:FLESHY,33:LEATHERY,34:SPONGY,35:FIBROUS,36:OILY,37:THORNY,38:VEINED,39:POCKED,40:BUMPY,41:JAGGED,42:DRIPPING,43:CRACKED,44:SHINEY,45:SMOOTH,46:STICKY,47:GOOEY,48:GUMMY,49:DRY,50:HORNY,52:MUCOUSY,54:SOFT,56:GLASSY,58:CRYSTALLINE,99:GNARLY" ) },
		{ "SKIN", new Table( "3:WAXY,6:FILMY,8:DULL,10:LEAFY,13:MUCOUSY,15:HAIRY,17:WARTY,20:HORNY,22:PLASTIC-LIKE,24:HARD,27:FLESHY,29:DRY,31:SPONGY,34:OILY,36:VEINED,38:POCKED,41:COARSE,43:BUMPY,45:CRUSTY,48:SLICK,50:CRACKED,52:SHINEY,55:WET,57:ROUGH,60:SOFT,63:DRIPPING,65:STICKY,68:GOOEY,71:GUMMY,73:CHITENOUS,76:LEATHERY,99:RUBBERY" ) },
		{ "HAIR", new Table( "7:FLOWING,14:WIREY,21:TANGLED,27:MATTED,33:BRISTLY,40:FUZZY,47:OILY,53:CURLY,59:SHINEY,66:SMOOTH,73:SOFT,99:COARSE" ) },
		{ "FUR", new Table( "8:WIREY,15:TANGLED,21:CURLY,27:MATTED,33:BRISTLY,40:FUZZY,47:OILY,53:COARSE,59:SHINEY,65:SMOOTH,99:SOFT" ) },
		{ "FEATHERED SURFACE", new Table( "6:CURLY,12:MATTED,17:CURLY,22:MATTED,28:DOWNY,33:BRISTLY,38:BRISTLY,44:FUZZY,49:FLESHY,55:LEATHERY,61:COARSE,66:SHINEY,72:SOFT,99:LEAFY" ) },
		{ "SCALES", new Table( "4:FILMY,6:DULL,8:BONEY,11:CHITENOUS,13:LEAFY,15:SERRATED,18:FUZZY,20:RUBBERY,22:HORNY,25:PLASTIC-LIKE,27:HARD,29:FLESHY,32:LEATHERY,34:DRY,36:SPONGY,39:OILY,41:GNARLY,43:STRIATED,46:BUMPY,48:CRUSTY,50:SLICK,53:JAGGED,55:GLASSY,57:SHINEY,60:CRYSTALLINE,62:ROUGH,64:SOFT,99:SMOOTH" ) },
		{ "ARMOR", new Table( "5:FILMY,9:DULL,12:BONEY,15:CHITENOUS,19:HORNY,23:PLASTIC-LIKE,26:HARD,29:LEATHERY,33:DRY,37:THORNY,40:GNARLY,43:STRIATED,47:POCKED,51:BUMPY,54:CRUSTY,57:SHINEY,61:CRYSTALLINE,65:ROCKY,68:ROUGH,99:SMOOTH" ) },
		{ "EXOSKELETON", new Table( "5:FILMY,9:DULL,13:BONEY,17:CHITENOUS,21:HORNY,25:PLASTIC-LIKE,29:HARD,33:LEATHERY,36:DRY,39:THORNY,42:GNARLY,45:STRIATED,48:POCKED,51:BUMPY,54:CRUSTY,57:SHINEY,61:CRYSTALLINE,65:ROUGH,99:SMOOTH" ) },
		{ "PATTERNED SURFACE", new Table( "99:-" ) },
	};

	// HP-OV WEAPONRY and DECORATIONS, by kind; #LEGS by shape (32767 is "many", 255 once stored)
	static readonly Table[] c_weapons = { new Table( "20:-,50:THORNS,70:POISON SAC,100:ACID SAC" ), new Table( "30:-,35:MANDIBLE,40:TEETH,45:BEAK,50:CLAWS,55:QUILLS,60:PINCER,65:TUSK,70:HORN,75:STINGER,80:POISON SAC,100:ACID SAC" ) };

	static readonly Table[] c_decorations = { new Table( "5:PROTRUSION,20:FLOWERS,24:FRUITS,28:BERRIES,33:PROJECTION,37:BARK,42:BULBS,62:LEAVES,72:VINES,82:STEM,100:BRANCHES" ), new Table( "5:SNOUT,8:FEELER,13:SHELL,17:PROTRUSION,19:CREST,21:MANE,28:EAR,37:COMPOUND EYE,44:EYE SPOT,51:EYE STALK,61:EYE,70:HEAD,75:MOUTH,78:WHISKERS,80:PLUMES,84:PROJECTION,85:TATA,86:HOOTER,87:BAZWANGO,90:FENDIBULAR EXTREMULATOR,100:TAIL" ) };

	static readonly Dictionary<string, Table> c_legs = new Dictionary<string, Table>
	{
		{ "BILATERAL", new Table( "10:1,15:1,33:2,43:4,55:6,67:8,77:12,78:3,79:5,82:7,85:9,88:11,100:10" ) },
		{ "RADIAL", new Table( "2:2,4:4,16:6,28:8,40:10,42:12,44:1,46:3,48:5,60:7,72:9,84:11,100:32767" ) },
		{ "SPHERICAL", new Table( "30:0,40:2,42:4,47:6,52:8,57:10,59:12,61:1,63:3,65:5,70:7,75:9,80:11,100:32767" ) },
		{ "IRREGULAR", new Table( "20:0,24:6,28:7,32:8,36:9,40:10,44:11,100:32767" ) },
		{ "AMORPHOUS", new Table( "100:0" ) },
	};

	// HP-OV ANIMAL.APPENDAGE by shape, and LEGG by the gravity class (1 to 6)
	static readonly Dictionary<string, Table> c_appendages = new Dictionary<string, Table>
	{
		{ "BILATERAL", new Table( "20:-,60:LEG,90:TENTACLE,95:PSEUDOPOD,100:CILIA" ) },
		{ "RADIAL", new Table( "5:-,35:LEG,75:TENTACLE,95:PSEUDOPOD,100:CILIA" ) },
		{ "SPHERICAL", new Table( "40:-,45:LEG,50:TENTACLE,80:PSEUDOPOD,100:CILIA" ) },
		{ "AMORPHOUS", new Table( "20:-,25:LEG,30:TENTACLE,70:PSEUDOPOD,100:CILIA" ) },
		{ "IRREGULAR", new Table( "20:-,25:LEG,30:TENTACLE,60:PSEUDOPOD,100:CILIA" ) },
	};

	static readonly Table[] c_legMotions =
	{
		null,
		new Table( "5:WALKING,15:HOPPING,45:JUMPING,100:LEAPING" ),
		new Table( "10:WALKING,25:HOPPING,55:JUMPING,100:LEAPING" ),
		new Table( "15:WALKING,35:HOPPING,65:JUMPING,100:LEAPING" ),
		new Table( "35:WALKING,65:HOPPING,85:JUMPING,100:LEAPING" ),
		new Table( "45:WALKING,75:HOPPING,90:JUMPING,100:LEAPING" ),
		new Table( "55:WALKING,85:HOPPING,95:JUMPING,100:LEAPING" ),
	};

	// LP-OV >MOVER (the index is slowest), HP-OV GROUNDED (by gravity class) and BOUYANT (by atmosphere density)
	static readonly string[] c_movers = { null, "OOZING", "CREEPING", "SLITHERING", "CRAWLING", "ROLLING", "MOVING", "WALKING", "HOPPING", "JUMPING", "LEAPING" };
	static readonly int[] c_grounded = { 0, 30, 10, 0, -30, -100, -100 };
	static readonly int[] c_buoyant = { 0, -50, 0, 20, 40, 50 };

	// LP-OV >SIZE and >PROP, and the heights in centimetres by size (animals SHORTEST/TALLEST, plants LOSTALK/STALKER)
	public static readonly string[] c_sizeWords = { null, "TINY", "SMALL", "BIG", "LARGE", "VERY LARGE", "HUGE", "MASSIVE", "HUMONGOUS", "GARGANTUAN" };
	public static readonly string[] c_proportionWords = { null, "FLATTENED", "FLAT", "WIDE", "THICK", "SQUAT", "ELONGATED", "NARROW", "SLENDER", "GRACEFUL", "THIN", "SPINDLY", "WILLOWY", "DELICATE" };
	static readonly int[] c_shortest = { 0, 1, 11, 36, 81, 181, 301, 601, 1034, 1367, 1734 };
	static readonly int[] c_tallest = { 0, 10, 35, 80, 180, 300, 600, 1033, 1366, 1733, 2133 };
	static readonly int[] c_lowestStalk = { 0, 1, 21, 71, 221, 701, 1801, 3701, 5867, 8734, 12434 };
	static readonly int[] c_highestStalk = { 0, 20, 70, 220, 700, 1800, 3700, 5866, 8733, 12433, 17066 };

	// LP-OV ?COLOR (27 hues; the pure ones get a tint or a glow) and LIGHTS
	public static readonly string[] c_hueWords = { null, "RED", "MARROON", "RUST-COLORED", "PINK", "REDDISH ORANGE", "BROWN", "BEIGE", "COPPER-COLORED", "ORANGE", "YELLOWISH ORANGE", "YELLOW", "LEMON YELLOW", "YELLOWISH GREEN", "BROWNISH GREEN", "GREEN", "LIME GREEN", "BLUE-GREEN", "TURQUOISE", "BLUE", "LAVENDAR", "VIOLET", "PURPLE", "WHITE", "MULTI-COLORED", "GREY", "BLACK", "VERY BLUE" };
	static readonly HashSet<string> c_pureHues = new HashSet<string> { "RED", "REDDISH ORANGE", "ORANGE", "YELLOWISH ORANGE", "YELLOW", "LEMON YELLOW", "YELLOWISH GREEN", "GREEN", "BLUE-GREEN", "BLUE", "VIOLET", "PURPLE", "VERY BLUE" };
	static readonly Table c_tints = new Table( "26:DARK,51:BRIGHT,100:LIGHT" );
	static readonly Table c_glows = new Table( "14:BLINDING,26:BRIGHT,39:GLOWING,51:LUMINESCENT,64:TRANSLUCENT,100:SHIMMERING" );

	// the STRING species of the words that matter: plurals (4, or an S at the end: ?s), mass nouns (5) and the one-per-body nouns (3)
	static readonly HashSet<string> c_massNouns = new HashSet<string> { "SKIN", "HAIR", "ARMOR", "FUR" };
	static readonly HashSet<string> c_onePerBody = new HashSet<string> { "BARK", "STEM", "SNOUT", "SHELL", "CREST", "MANE", "HEAD", "BAZWANGO", "TAIL" };

	static bool IsPlural( string word )
	{
		return ( word != null ) && ( word.EndsWith( "S" ) || ( word == "CILIA" ) || ( word == "TEETH" ) );
	}

	// REGIONS' base crowding by temperature class (Sub-Arctic to Inferno, the port's GD_Planet.m_temperatureIdA..B), read with C@: -100 is 156
	static readonly int[] c_regionBase = { 156, 20, 50, 70, 10, 156 };

	// the PLANET file's seed (word 0x05) and mass (word 0x02) of each planet type that has life (each is one planet)
	static readonly Dictionary<int, int> c_seeds = new Dictionary<int, int>
	{
		{ 24, 699 }, { 25, 799 }, { 26, 899 }, { 27, 999 }, { 28, 109 }, { 29, 119 }, { 30, 119 }, { 31, 139 }, { 32, 197 }, { 33, 159 }, { 34, 169 }, { 35, 179 },
		{ 36, 189 }, { 37, 209 }, { 38, 205 }, { 39, 229 }, { 40, 239 }, { 41, 249 }, { 42, 259 }, { 43, 10101 }, { 46, 309 }, { 47, 319 }, { 48, 329 }, { 49, 339 },
		{ 50, 349 }, { 51, 359 }, { 52, 369 }, { 53, 379 }, { 54, 389 }, { 55, 409 }, { 56, 419 }, { 57, 429 }, { 58, 439 }, { 59, 449 }, { 60, 459 },
	};

	static readonly Dictionary<int, int> c_masses = new Dictionary<int, int>
	{
		{ 24, 95 }, { 25, 250 }, { 26, 60 }, { 27, 80 }, { 28, 109 }, { 29, 140 }, { 30, 76 }, { 31, 180 }, { 32, 250 }, { 33, 105 }, { 34, 100 }, { 35, 55 },
		{ 36, 180 }, { 37, 72 }, { 38, 94 }, { 39, 125 }, { 40, 400 }, { 41, 280 }, { 42, 88 }, { 43, 115 }, { 46, 50 }, { 47, 89 }, { 48, 210 }, { 49, 99 },
		{ 50, 77 }, { 51, 95 }, { 52, 370 }, { 53, 178 }, { 54, 100 }, { 55, 65 }, { 56, 200 }, { 57, 140 }, { 58, 108 }, { 59, 85 }, { 60, 790 },
	};

	// ---- a species record (the CREATURE file's fields the port uses)

	public class Creature
	{
		public int m_classFirst;
		public int m_classLast;
		public int m_orderFirst;
		public int m_orderLast;
		public bool m_isClassFirst;
		public bool m_isOrderFirst;
		public string m_shape;
		public string m_exterior;
		public int m_region;
		public int m_sizeIndex;
		public int m_niche;
		public int m_reddest;
		public int m_bluest;
		public string m_texture;
		public string m_weapon;
		public int m_weaponCount;
		public string m_decoration;
		public int m_decorationCount;
		public string m_airAppendage;
		public int m_airCount;
		public string m_groundAppendage;
		public int m_groundCount;
		public string m_groundMotion;
		public string m_airMotion;
		public int m_slowest;
		public int m_fastest;
		public int m_stunEffect;
		public int m_hitPoints;
		public int m_intelligence;
		public int m_aggression;
		public int m_heightCm;
		public int m_volume;
		public string m_proportion;
		public int m_proportionIndex;
		public int m_icon;
		public string m_resembles;
		public string m_size;
		public string m_hue;
		public string m_tint;
		public string m_glow;

		public Creature Copy()
		{
			return (Creature) MemberwiseClone();
		}
	}

	// what one landing's generation gives: the species records (0 to the last), the individuals of a species per block of each region, the carpet of the band
	// the vehicle lands in, and the regions' crowding
	public class Result
	{
		public List<Creature> m_species = new List<Creature>();
		public int[] m_individualsPerBlock = new int[ 6 ];
		public int[] m_crowding = new int[ 6 ];
		public string m_carpet;
		public int m_lowestRegion;
		public int m_highestRegion;
		public int m_drawsInVita;

		// the planet type's seed (the PLANET file's word 0x05)
		public int m_seed;

		// how many of the species records are ever placed: all but the last (WEE91 loops 0 to PLHI - 1), and the one record of a taxonomy of one (the loop
		// runs once)
		public int GetPlacedSpeciesCount()
		{
			return ( m_species.Count == 0 ) ? 0 : Mathf.Max( 1, m_species.Count - 1 );
		}

		// the region (temperature class) of a row of the surface (0 to 960 from pole to pole), as SEED-OV WEDAF picks it: the rows folded at the equator, so
		// the coldest region is at the poles and the hottest at the equator
		public int GetRegionAt( int surfaceRow )
		{
			var rowsFromPole = Mathf.Clamp( ( surfaceRow > 480 ) ? 960 - surfaceRow : surfaceRow, 0, 480 );
			var bands = m_highestRegion - m_lowestRegion + 1;

			return Mathf.Clamp( m_lowestRegion + rowsFromPole / ( 480 / Mathf.Max( 1, bands ) + 1 ), m_lowestRegion, m_highestRegion );
		}

		// true if lifeforms are placed in this region: a species placed there, and individuals per block to place
		public bool HasLifeIn( int region )
		{
			if ( ( region < m_lowestRegion ) || ( region > m_highestRegion ) || ( m_individualsPerBlock[ region ] <= 0 ) )
			{
				return false;
			}

			for ( var r = 0; r < GetPlacedSpeciesCount(); r++ )
			{
				if ( m_species[ r ].m_region == region )
				{
					return true;
				}
			}

			return false;
		}

		// the seed of a block's placement, as !XYSEED makes it (FRND of each coordinate and the global seed, which the port takes from the planet type's seed)
		public int GetBlockSeed( int column, int row )
		{
			return ( ( column * 31421 + 6927 ) ^ ( row * 31421 + 6927 ) ^ m_seed ) & 0xffff;
		}
	}

	// the original's bio density byte (0 to 255) from the port's percentage (through the flare's sterilising, PlayerData.GetBioDensityOf)
	public static int GetBioByte( GD_Planet planet )
	{
		return Mathf.Clamp( Mathf.RoundToInt( PlayerData.GetBioDensityOf( planet ) * 255.0f / 100.0f ), 0, 255 );
	}

	// true if the shipped game has this planet's type in its life tables
	public static bool HasRecord( GD_Planet planet )
	{
		return ( planet != null ) && c_seeds.ContainsKey( planet.m_planetTypeId );
	}

	/// <summary>
	/// The lifeforms of a planet as the shipped game makes them (VITA-OV DIO, PLSET-OV SETC, HP-OV C+OPARMS, then LP-OV SPEC.PARM for each species): none
	/// without life. The carpet draw of SETC depends on the band the vehicle lands in; the port makes the species as if it landed at the equator, so that they
	/// are the same on every landing.
	/// </summary>
	public static Result Make( GD_Planet planet )
	{
		var result = new Result();
		var bio = GetBioByte( planet );

		if ( ( bio <= 0 ) || !HasRecord( planet ) )
		{
			return result;
		}

		var lowest = Mathf.Clamp( planet.m_temperatureIdA, 0, 5 );
		var highest = Mathf.Clamp( planet.m_temperatureIdB, lowest, 5 );
		var mass = c_masses[ planet.m_planetTypeId ];
		var random = new Generator( c_seeds[ planet.m_planetTypeId ] );

		result.m_lowestRegion = lowest;
		result.m_highestRegion = highest;
		result.m_seed = c_seeds[ planet.m_planetTypeId ];

		// ---- VITA-OV DIO: crowding (WEC19, WEBD3)
		var crowd = new int[ 6 ];

		for ( var t = lowest; t <= highest; t++ )
		{
			var n = c_regionBase[ t ] + bio / 4;

			crowd[ t ] = Mathf.Min( random.Range( n, 2 * n ), 256 );
		}

		// the niche flags of each region (WED13, WECCB): carpet, producers, herbivores, carnivores
		var flags = new int[ 6 ];

		for ( var t = lowest; t <= highest; t++ )
		{
			var c = crowd[ t ];
			var carpet = ( c < 15 ) ? 1 : 0;
			var producers = 0;

			if ( carpet == 0 )
			{
				if ( ( c > 14 ) && ( c < 50 ) )
				{
					var f = ( 2 * c > random.Percent() ) ? 1 : 0;

					producers = f;
					carpet = f;

					if ( f == 0 )
					{
						var k = random.Coin();

						if ( Mathf.Max( 0, 100 - 2 * c ) / 2 > random.Percent() )
						{
							k = 1 - k;
						}

						producers = k;
						carpet = 1 - k;
					}
				}
				else
				{
					carpet = 1;
					producers = 1;
				}
			}

			var herbivores = ( c > random.Percent() ) ? 1 : 0;
			var carnivores = ( herbivores != 0 ) && ( c > random.Percent() ) ? 1 : 0;

			flags[ t ] = carpet | ( producers << 1 ) | ( herbivores << 2 ) | ( carnivores << 3 );
		}

		// the niches of each region (WEEAD, WEE43): drawn until every level present has had two
		var slots = new int[ 6, 8 ];

		for ( var t = lowest; t <= highest; t++ )
		{
			var carpetBit = flags[ t ] & 1;
			var key = flags[ t ] & 0x0e;

			if ( ( key == 0 ) || !c_niches.ContainsKey( key ) )
			{
				continue;
			}

			var need = new[] { ( ( key >> 1 ) & 1 ) * 2, ( ( key >> 2 ) & 1 ) * 2, ( ( key >> 3 ) & 1 ) * 2 };

			for ( var tries = 0; ( tries < 10000 ) && ( need[ 0 ] + need[ 1 ] + need[ 2 ] > 0 ); tries++ )
			{
				var v = int.Parse( c_niches[ key ].Pick( random.Percent() ) ) | carpetBit;
				var known = false;

				for ( var s = 0; s < 8; s++ )
				{
					known |= slots[ t, s ] == v;
				}

				if ( !known )
				{
					for ( var s = 0; s < 8; s++ )
					{
						if ( slots[ t, s ] == 0 )
						{
							slots[ t, s ] = v;
							break;
						}
					}
				}

				for ( var i = 0; i < 3; i++ )
				{
					if ( ( need[ i ] > 0 ) && ( ( ( v >> ( i + 1 ) ) & 1 ) != 0 ) )
					{
						need[ i ]--;
					}
				}
			}
		}

		// the classes of plants, then of animals (WEF25: always both)
		var most = 0;

		for ( var t = lowest; t <= highest; t++ )
		{
			most = Mathf.Max( most, crowd[ t ] );
		}

		var plantClasses = ( most > 80 ) ? random.Range( 2, 5 ) : random.Range( 1, Mathf.Max( 1, most / 15 ) );
		var animalClasses = ( most > 80 ) ? random.Range( 2, 5 ) : random.Range( 1, Mathf.Max( 1, most / 15 ) );

		// the units of each niche slot (WEF95)
		var units = new int[ 6, 8 ];

		for ( var t = lowest; t <= highest; t++ )
		{
			for ( var s = 0; s < 8; s++ )
			{
				units[ t, s ] = ( ( slots[ t, s ] & 0x0e ) != 0 ) ? random.Range( 1, Mathf.Min( crowd[ t ] / 10, 5 ) + 1 ) : 0;
			}
		}

		// the species of each order in a region (WEFE1, WEFC5: no guard for a region without units in the original, which divides by 0 - the port takes 1)
		var speciesPerOrder = new int[ 6 ];

		for ( var t = lowest; t <= highest; t++ )
		{
			var total = 0;

			for ( var s = 0; s < 7; s++ )
			{
				total += units[ t, s ];
			}

			var drawn = ToSigned16( random.Range( total, Mathf.Min( crowd[ t ], 40 ) ) );

			speciesPerOrder[ t ] = ( total > 0 ) ? drawn / total : 1;
		}

		// the units of each kind in each region (WF041)
		var totals = new int[ 6, 2 ];
		var remaining = new int[ 6, 2 ];

		for ( var t = lowest; t <= highest; t++ )
		{
			for ( var s = 0; s < 7; s++ )
			{
				var niche = slots[ t, s ] & 0x0e;

				if ( niche != 0 )
				{
					totals[ t, KindOf( niche ) ] += units[ t, s ];
					remaining[ t, KindOf( niche ) ] += units[ t, s ];
				}
			}
		}

		// ---- the taxonomy (WF334): the animals' classes, orders and species first, then the plants'
		var ordersPerClass = new int[ 2 ];

		for ( var kind = 0; kind < 2; kind++ )
		{
			var sum = 0;

			for ( var t = lowest; t <= highest; t++ )
			{
				sum += totals[ t, kind ];
			}

			ordersPerClass[ kind ] = sum / Mathf.Max( 1, ( kind == c_plants ) ? plantClasses : animalClasses ) + 1;
		}

		var records = new List<Creature> { new Creature() };
		var last = 0;

		MakeKind( c_animals, animalClasses, ordersPerClass[ c_animals ], random, records, ref last, lowest, highest, slots, units, remaining, speciesPerOrder );

		if ( last != 0 )
		{
			last++;
			SetRecord( records, last, new Creature() );
		}

		MakeKind( c_plants, plantClasses, ordersPerClass[ c_plants ], random, records, ref last, lowest, highest, slots, units, remaining, speciesPerOrder );

		var plhi = Mathf.Min( last, records.Count - 1 );

		// each order's niche and region to its other species (WF382)
		for ( var r = 0; r <= plhi; r++ )
		{
			var order = records[ r ];

			if ( order.m_isOrderFirst )
			{
				for ( var member = r + 1; ( member <= order.m_orderLast ) && ( member <= plhi ); member++ )
				{
					records[ member ].m_niche = order.m_niche;
					records[ member ].m_region = order.m_region;
				}
			}
		}

		// the individuals of a species in each block (WF40C)
		for ( var t = lowest; t <= highest; t++ )
		{
			result.m_individualsPerBlock[ t ] = ( crowd[ t ] != 0 ) ? Mathf.Max( 2, crowd[ t ] / ( plhi + 10 ) ) : 0;
			result.m_crowding[ t ] = crowd[ t ];
		}

		// ---- PLSET-OV SETC: the carpet of the band the vehicle lands in (the port: the equator's band), one draw only if that region has a carpet niche
		var band = lowest + 480 / ( 480 / ( highest - lowest + 1 ) + 1 );

		band = Mathf.Clamp( band, lowest, highest );

		var hasCarpet = false;

		for ( var s = 0; s < 8; s++ )
		{
			hasCarpet |= ( slots[ band, s ] & 1 ) != 0;
		}

		if ( hasCarpet )
		{
			result.m_carpet = new[] { "MOSS-LIKE", "GRASS-LIKE", "FUNGOID", "LICHEN-LIKE", "GELATINOUS", "FIBROUS", "LEAFY", "SPONGY" }[ random.Range( 0, 8 ) ];
		}

		// ---- HP-OV C+OPARMS (none with a single record: the original waits and stops)
		if ( plhi > 0 )
		{
			ClassParameters( records, plhi, random );
			OrderParameters( records, plhi, random, mass, planet );
		}

		// ---- LP-OV SPEC.PARM for each species (SEED = its record number), after its order's fields are copied to it (SEED-OV WF02B)
		for ( var r = 0; r <= plhi; r++ )
		{
			CopyOrderFields( records, r );
			SpeciesParameters( records[ r ], r, mass );
			result.m_species.Add( records[ r ] );
		}

		return result;
	}

	// a record at an index, growing the list as the original's file of 100 records is filled (the original hangs past 99: the port stops there)
	static void SetRecord( List<Creature> records, int index, Creature creature )
	{
		while ( records.Count <= index )
		{
			records.Add( new Creature() );
		}

		records[ index ] = creature;
	}

	// the classes, orders and species of one kind (WF2DA, WF286, WF23C)
	static void MakeKind( int kind, int classes, int ordersPerClass, Generator random, List<Creature> records, ref int last, int lowest, int highest, int[,] slots, int[,] units, int[,] remaining, int[] speciesPerOrder )
	{
		const int c_mostRecords = 99;

		for ( var ci = 1; ci <= Mathf.Max( 1, classes - 1 ); ci++ )
		{
			if ( Remaining( remaining, kind, lowest, highest ) == 0 )
			{
				break;
			}

			if ( ci > 1 )
			{
				last++;
			}

			if ( last > c_mostRecords )
			{
				return;
			}

			var classFirst = last;

			SetRecord( records, last, new Creature { m_classFirst = last, m_isClassFirst = true } );

			for ( var oi = 1; oi <= ordersPerClass; oi++ )
			{
				if ( Remaining( remaining, kind, lowest, highest ) == 0 )
				{
					break;
				}

				if ( oi > 1 )
				{
					last++;
				}

				if ( last > c_mostRecords )
				{
					last = c_mostRecords;
					return;
				}

				Pick( kind, random, lowest, highest, slots, units, remaining, out var niche, out var region );

				if ( oi > 1 )
				{
					SetRecord( records, last, new Creature() );
				}

				var orderFirst = last;

				records[ orderFirst ].m_niche = niche;
				records[ orderFirst ].m_region = region;
				records[ orderFirst ].m_orderFirst = orderFirst;
				records[ orderFirst ].m_isOrderFirst = true;

				for ( var si = 1; si <= Mathf.Max( 1, speciesPerOrder[ region ] ); si++ )
				{
					if ( si > 1 )
					{
						if ( last >= c_mostRecords )
						{
							break;
						}

						last++;
						SetRecord( records, last, new Creature() );
					}
				}

				records[ orderFirst ].m_orderLast = last;
			}

			records[ classFirst ].m_classLast = last;
		}
	}

	static int Remaining( int[,] remaining, int kind, int lowest, int highest )
	{
		var sum = 0;

		for ( var t = lowest; t <= highest; t++ )
		{
			sum += remaining[ t, kind ];
		}

		return sum;
	}

	// an order's niche and region (WF155, WF119): a region with units of the kind left, and a slot of the kind in it
	static void Pick( int kind, Generator random, int lowest, int highest, int[,] slots, int[,] units, int[,] remaining, out int niche, out int region )
	{
		niche = 0;
		region = lowest;

		for ( var tries = 0; tries < 100000; tries++ )
		{
			var t = random.Range( lowest, highest + 1 );

			if ( ( t < lowest ) || ( t > highest ) || ( remaining[ t, kind ] <= 0 ) )
			{
				continue;
			}

			for ( var slotTries = 0; slotTries < 100000; slotTries++ )
			{
				var s = random.Range( 0, 7 );

				if ( ( KindOf( slots[ t, s ] & 0x0e ) == kind ) && ( units[ t, s ] > 0 ) )
				{
					remaining[ t, kind ]--;
					units[ t, s ]--;
					niche = slots[ t, s ] & 0x0e;
					region = t;

					return;
				}
			}

			return;
		}
	}

	// HP-OV class.parms: a class's shape and exterior (shaper, pelts), for every record of the class
	static void ClassParameters( List<Creature> records, int plhi, Generator random )
	{
		for ( var r = 0; r <= plhi; )
		{
			var first = records[ r ];
			var kind = KindOf( first.m_niche );
			var shape = c_shapes[ kind ].Pick( random.Percent() );
			string exterior = "SURFACE";

			if ( kind == c_animals )
			{
				exterior = null;

				for ( var tries = 0; ( tries < 10000 ) && ( exterior == null ); tries++ )
				{
					exterior = c_exteriors[ shape ].Pick( random.Percent() );
				}
			}

			var classLast = Mathf.Clamp( first.m_classLast, r, plhi );

			for ( var member = r; member <= classLast; member++ )
			{
				records[ member ].m_shape = shape;
				records[ member ].m_exterior = exterior;
			}

			r = classLast + 1;
		}
	}

	// the gravity class (HP-OV: plan-mass / 100, at most 5, plus 1)
	static int GravityClass( int mass )
	{
		return Mathf.Min( mass / 100, 5 ) + 1;
	}

	// HP-OV order.parms (WF4A6): an order's size, colours, weapon, decoration, flight, mobility, appendage and movement, and texture
	static void OrderParameters( List<Creature> records, int plhi, Generator random, int mass, GD_Planet planet )
	{
		var g = GravityClass( mass );
		var atmosphere = Mathf.Clamp( planet.m_atmosphereDensityId, 0, 5 );

		for ( var r = 0; r <= plhi; )
		{
			var order = records[ r ];
			var kind = KindOf( order.m_niche );
			var shape = order.m_shape ?? "BILATERAL";

			// sizing
			random.m_peak = ( 6 - g ) * 20;
			order.m_sizeIndex = Mathf.Clamp( random.Slip( 10, 100 ) / 12 + 1, 1, 9 );

			// dye: animals any hue, plants their own range or the star's
			int lo = 1, hi = 26;

			if ( kind == c_plants )
			{
				if ( random.Coin() == 1 )
				{
					lo = 23;
					hi = 26;
				}
				else
				{
					var star = DataController.m_instance.m_gameData.m_starList[ Mathf.Clamp( planet.m_starId, 0, DataController.m_instance.m_gameData.m_starList.Length - 1 ) ];
					var letter = string.IsNullOrEmpty( star.m_class ) ? 'G' : char.ToUpper( star.m_class[ 0 ] );
					var v = ( letter == 'M' ) ? 1 : ( ( letter == 'K' ) ? 9 : ( ( letter == 'G' ) ? 11 : ( ( letter == 'F' ) ? 24 : ( ( letter == 'A' ) ? 15 : ( ( letter == 'B' ) ? 19 : 27 ) ) ) ) );

					if ( v > 13 )
					{
						lo = 14;
						hi = 22;
					}
					else if ( v < 11 )
					{
						lo = 1;
						hi = 10;
					}
					else
					{
						lo = 8;
						hi = 19;
					}
				}
			}

			order.m_reddest = random.Range( lo, hi );
			order.m_bluest = random.Range( order.m_reddest, Mathf.Min( hi + 1, 27 ) );

			// fortify
			order.m_weapon = c_weapons[ kind ].Pick( random.Percent() );
			order.m_weaponCount = ( order.m_weapon == null ) ? 0 : ( IsPlural( order.m_weapon ) ? 255 : ( c_massNouns.Contains( order.m_weapon ) ? 0 : 1 ) );

			// decorate and trimming (a count only for a singular decoration)
			order.m_decoration = c_decorations[ kind ].Pick( random.Percent() );

			if ( order.m_decoration == null )
			{
				order.m_decorationCount = 0;
			}
			else if ( c_onePerBody.Contains( order.m_decoration ) )
			{
				order.m_decorationCount = 1;
			}
			else if ( !IsPlural( order.m_decoration ) )
			{
				var n = c_legs[ shape ].PickNumber( random.Percent() );

				order.m_decorationCount = ( n < 16 ) ? n + 1 : 255;
			}
			else
			{
				order.m_decorationCount = 0;
			}

			// elevate: small enough for the gravity and the air, and a roll over X
			var x = Mathf.Max( 0, c_grounded[ g ] + c_buoyant[ atmosphere ] );
			var limit = ( atmosphere - g + 5 ) * ( atmosphere - g + 5 ) / 9;

			order.m_airAppendage = null;
			order.m_airMotion = null;
			order.m_airCount = 0;

			if ( order.m_sizeIndex < limit )
			{
				if ( kind == c_animals )
				{
					if ( x < random.Percent() )
					{
						if ( random.Percent() < 80 )
						{
							order.m_airAppendage = "WING";
							order.m_airMotion = "FLYING";
						}
						else
						{
							order.m_airAppendage = "FLOAT SAC";
							order.m_airMotion = "FLOATING";
						}

						order.m_airCount = ( random.Coin() + 1 ) * 2;
					}
				}
				else if ( x < random.Percent() )
				{
					if ( random.Coin() == 1 )
					{
						order.m_airAppendage = "FLOAT SAC";
						order.m_airMotion = "FLOATING";
						order.m_airCount = 1;
					}
				}
			}

			// nubile: one draw always, then the plants' mobility
			random.m_peak = 30;

			var mobility = random.Slip( 0, 40 ) + 25;

			if ( kind == c_animals )
			{
				order.m_groundMotion = "MOBILE";
			}
			else
			{
				if ( order.m_niche == 2 )
				{
					mobility = Mathf.Max( 0, mobility - 25 );
				}

				order.m_groundMotion = ( mobility > random.Percent() ) ? "MOBILE" : "SESSILE";
			}

			// stroll
			order.m_groundAppendage = null;

			if ( kind == c_animals )
			{
				order.m_groundAppendage = c_appendages[ shape ].Pick( random.Percent() );
			}
			else if ( order.m_groundMotion == "MOBILE" )
			{
				if ( random.Percent() < 35 )
				{
					order.m_groundMotion = "SESSILE";
				}
				else
				{
					order.m_groundAppendage = "TENTACLE";
				}
			}

			// animate
			order.m_groundCount = 0;

			if ( order.m_groundAppendage != null )
			{
				if ( IsPlural( order.m_groundAppendage ) )
				{
					order.m_groundCount = 255;
				}
				else if ( c_massNouns.Contains( order.m_groundAppendage ) )
				{
					order.m_groundCount = 255;
				}
				else
				{
					var n = c_legs[ shape ].PickNumber( random.Percent() );

					order.m_groundCount = ( n > 255 ) ? 255 : n;
				}

				if ( order.m_groundCount == 0 )
				{
					order.m_groundAppendage = null;
				}
			}

			// movem
			if ( kind == c_animals )
			{
				if ( order.m_groundCount == 0 )
				{
					order.m_groundMotion = "MOVING";
				}
				else if ( order.m_groundAppendage == "TENTACLE" )
				{
					order.m_groundMotion = "CRAWLING";
				}
				else if ( order.m_groundAppendage == "PSEUDOPOD" )
				{
					order.m_groundMotion = "OOZING";
				}
				else if ( order.m_groundAppendage == "CILIA" )
				{
					order.m_groundMotion = "CREEPING";
				}
				else if ( order.m_groundAppendage == "LEG" )
				{
					order.m_groundMotion = c_legMotions[ g ].Pick( random.Percent() );
				}
				else
				{
					order.m_groundMotion = "MOVING";
				}
			}
			else
			{
				order.m_groundMotion = ( order.m_groundMotion == "MOBILE" ) ? "CRAWLING" : "SESSILE";
			}

			// texturize (plants on the SURFACE row)
			var textures = c_textures[ ( kind == c_plants ) ? "SURFACE" : ( order.m_exterior ?? "SURFACE" ) ];

			order.m_texture = textures.Pick( random.Percent() );

			r = Mathf.Clamp( order.m_orderLast, r, plhi ) + 1;
		}
	}

	// a species takes its order's fields (SEED-OV WF02B, through WF00B, WEF89 and WEFC5)
	static void CopyOrderFields( List<Creature> records, int r )
	{
		var record = records[ r ];
		var orderFirst = r;

		for ( var back = r; back >= 0; back-- )
		{
			if ( records[ back ].m_isOrderFirst )
			{
				orderFirst = back;
				break;
			}
		}

		if ( orderFirst == r )
		{
			return;
		}

		var order = records[ orderFirst ];

		record.m_sizeIndex = order.m_sizeIndex;
		record.m_weaponCount = order.m_weaponCount;
		record.m_weapon = order.m_weapon;
		record.m_reddest = order.m_reddest;
		record.m_bluest = order.m_bluest;
		record.m_decorationCount = order.m_decorationCount;
		record.m_decoration = order.m_decoration;
		record.m_airMotion = order.m_airMotion;
		record.m_airCount = order.m_airCount;
		record.m_airAppendage = order.m_airAppendage;
		record.m_groundMotion = order.m_groundMotion;
		record.m_groundCount = order.m_groundCount;
		record.m_groundAppendage = order.m_groundAppendage;
		record.m_texture = order.m_texture;
		record.m_orderFirst = orderFirst;
		record.m_orderLast = order.m_orderLast;
	}

	// LP-OV madness: the range of aggression and intelligence by niche
	static void Madness( int niche, out int lo, out int hi )
	{
		if ( niche == 2 )
		{
			lo = 1;
			hi = 10;
		}
		else if ( ( niche == 4 ) || ( niche == 6 ) )
		{
			lo = 1;
			hi = 60;
		}
		else
		{
			lo = 50;
			hi = 100;
		}
	}

	// LP-OV SPEC.PARM: one species' proportion, aggression, hit points, stun effect, movement, icon, height, volume, intelligence, resembles and colour
	static void SpeciesParameters( Creature creature, int recordNumber, int mass )
	{
		var random = new Generator( recordNumber );
		var kind = KindOf( creature.m_niche );
		var size = Mathf.Clamp( creature.m_sizeIndex, 1, 9 );

		// set-size
		creature.m_size = c_sizeWords[ size ];

		// squish: the proportion within the planet's range by its mass (PLSET-OV WF03E); index 0 would divide by 0 in highness, the port takes 1
		var g2 = Mathf.Clamp( mass / 100, 1, 6 );
		var flattest = 6 - g2;
		var narrowest = Mathf.Min( 14 - g2, 13 );
		var p = random.Range( flattest, narrowest + 1 );

		p = Mathf.Clamp( p, 1, 13 );
		creature.m_proportionIndex = p;
		creature.m_proportion = c_proportionWords[ p ];

		// anger, durable, stunable
		Madness( creature.m_niche, out var lo, out var hi );

		random.m_peak = 128;
		creature.m_aggression = random.Slip( lo, hi );

		random.m_peak = 0;
		creature.m_hitPoints = ( random.Slip( 0, 85 ) + 18 * creature.m_sizeIndex + ( creature.m_aggression >> 1 ) ) / 3;
		creature.m_stunEffect = ( random.Slip( 0, 85 ) + 18 * creature.m_sizeIndex + ( creature.m_aggression >> 1 ) ) / 3;

		// movers
		creature.m_slowest = System.Array.IndexOf( c_movers, creature.m_groundMotion );
		creature.m_slowest = Mathf.Max( 0, creature.m_slowest );
		creature.m_fastest = ( creature.m_airMotion == "FLOATING" ) ? 5 : ( ( creature.m_airMotion != null ) ? 10 : 0 );

		// set.icon (a coin only for a bilateral plant)
		var shape = creature.m_shape ?? "BILATERAL";
		var legs = creature.m_groundCount;

		if ( ( shape == "RADIAL" ) || ( shape == "SPHERICAL" ) )
		{
			creature.m_icon = ( legs == 0 ) ? 12 : ( ( legs <= 2 ) ? 13 : 14 );
		}
		else if ( ( shape == "AMORPHOUS" ) || ( shape == "IRREGULAR" ) )
		{
			creature.m_icon = ( legs == 0 ) ? 15 : 16;
		}
		else if ( kind == c_plants )
		{
			var coin = random.Coin();

			creature.m_icon = ( creature.m_groundMotion == "CRAWLING" ) ? ( ( coin == 1 ) ? 4 : 5 ) : coin + 2 * Mathf.Min( 2, ( 9 - size ) / 3 );
		}
		else
		{
			creature.m_icon = ( legs == 0 ) ? ( ( creature.m_proportion == "ELONGATED" ) ? 6 : 7 ) : ( ( legs == 1 ) ? 8 : ( ( legs == 2 ) ? 9 : ( ( legs <= 4 ) ? 10 : 11 ) ) );
		}

		// highness: the height in centimetres, and the volume from it
		creature.m_heightCm = ( kind == c_plants ) ? random.Range( c_lowestStalk[ size ], c_highestStalk[ size ] + 1 ) : random.Range( c_shortest[ size ], c_tallest[ size ] + 1 );
		creature.m_volume = Mathf.Max( 1, creature.m_heightCm / p );

		// smarts
		random.m_peak = 128;
		creature.m_intelligence = random.Slip( lo, hi );

		// sculpts: four structure words drawn (not kept by the port)
		for ( var i = 0; i < 4; i++ )
		{
			random.Percent();
		}

		// similarity
		creature.m_resembles = Resembles( creature, kind, random );

		// modify: the proportions of the appendages (drawn, not kept)
		foreach ( var appendage in new[] { creature.m_decoration, creature.m_weapon, creature.m_airAppendage, creature.m_groundAppendage } )
		{
			if ( ( appendage != null ) && ( appendage != "TAIL" ) )
			{
				random.Range( 1, 14 );
			}
		}

		// colorant: the body's hue, and a tint or a glow for a pure hue
		var hue = Mathf.Clamp( random.Range( creature.m_reddest, creature.m_bluest + 1 ), 1, 27 );

		creature.m_hue = c_hueWords[ hue ];

		if ( c_pureHues.Contains( creature.m_hue ) )
		{
			if ( random.Coin() == 1 )
			{
				creature.m_tint = c_tints.Pick( random.Percent() );
			}
			else if ( random.Coin() == 1 )
			{
				creature.m_glow = c_glows.Pick( random.Percent() );
			}
		}
	}

	// LP-OV similarity: what it resembles (null: the shape word is used) - only the rules the shipped code can reach
	static string Resembles( Creature creature, int kind, Generator random )
	{
		if ( kind == c_plants )
		{
			if ( ( creature.m_airAppendage == "FLOAT SAC" ) && ( creature.m_airCount == 1 ) )
			{
				return "BALLOON-LIKE";
			}

			if ( ( creature.m_groundAppendage == "TENTACLE" ) && ( creature.m_groundCount > 5 ) )
			{
				return "ANEMONE-LIKE";
			}

			return ( 20 < random.Percent() ) ? "FUNGOID" : "PLANT-LIKE";
		}

		if ( ( creature.m_proportion == "NARROW" ) && ( creature.m_groundAppendage == "LEG" ) && ( creature.m_groundCount > 4 ) )
		{
			return "SPIDER-LIKE";
		}

		if ( creature.m_exterior == "SCALES" )
		{
			return ( creature.m_sizeIndex > 3 ) ? "DINOSAUR-LIKE" : ( ( creature.m_decoration == "SHELL" ) ? "TURTLE-LIKE" : "REPTILIAN" );
		}

		if ( creature.m_texture == "MUCOUSY" )
		{
			return ( creature.m_decoration == "SHELL" ) ? "SNAIL-LIKE" : "SLUG-LIKE";
		}

		// (the size test compares a field's address, so every furred animal is rodent-like: INFERRED from the record buffers' addresses)
		if ( creature.m_exterior == "FUR" )
		{
			return "RODENT-LIKE";
		}

		return null;
	}
}

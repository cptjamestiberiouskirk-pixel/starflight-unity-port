using UnityEngine;

// the shipped game's surface icons and their key (LISTICONS ICONS, roadmap 8.27): the 27 pictures of its ICON1:1 file and the names of its ICON-NAME file, which
// the terrain vehicle's Icons button shows on two pages, icons 0 to 14, then 15 to 26 with the elevation bar - each picture is two 8 by 8 layers, the second
// drawn over the first, each a colour and eight rows (the top row first, the left pixel in the high bit), the colours the shipped game's EGA colours (its
// C>EGA table turns the pictures' CGA colour bytes into them), drawn on the key's light grey (GREY1)
public static class TerrainIconKey
{
	// the icons the port's marks use
	public const int c_largeProducer = 0;
	public const int c_mediumProducer = 2;
	public const int c_smallProducer = 4;
	public const int c_snakelike = 6;
	public const int c_flyingLifeform = 17;
	public const int c_floatingLifeform = 18;
	public const int c_deadLifeform = 19;
	public const int c_specimen = 20;
	public const int c_mineral = 21;
	public const int c_artifact = 22;
	public const int c_recentRuins = 23;
	public const int c_ancientRuins = 24;
	public const int c_ship = 25;
	public const int c_terrainVehicle = 26;

	// how many icons there are, and how many of them are on the key's first page (SEE-BLTS 0 to 15, then 15 to 27)
	public const int c_iconCount = 27;
	public const int c_firstPageCount = 15;

	// the size of a picture, in pixels
	const int c_size = 8;

	// the light grey the key is drawn on (GREY1)
	const int c_backgroundColour = 7;

	// the names (ICON-NAME), in the port's sentence case
	static readonly string[] s_names =
	{
		"Large producer", "Large producer", "Medium size producer", "Medium size producer", "Small producer", "Small producer", "Worm/snake-like", "Bilateral no legs/tentacles",
		"Bilateral monopedal", "Bilateral bipedal", "Bilateral tri/quadrapedal", "Bilateral multipedal", "Radial no legs/tentacles", "Radial monopedal", "Radial multipedal",
		"Amorphous", "Amorphous multipedal", "Flying lifeform", "Floating lifeform", "Dead lifeform", "Specimen", "Mineral", "Artifact", "Recent ruins", "Ancient ruins", "Ship",
		"Terrain vehicle"
	};

	// the pictures (ICON1:1): for each, the first layer's colour and rows, then the second's
	static readonly byte[] s_pictures =
	{
		// 0 large producer
		4, 0x20, 0x00, 0x40, 0x08, 0x10, 0x24, 0x10, 0x10, 3, 0x5c, 0x38, 0xbe, 0x74, 0xa6, 0xda, 0x00, 0x00,
		// 1 large producer
		0, 0x20, 0x00, 0x04, 0x48, 0x10, 0x20, 0x10, 0x10, 3, 0x5c, 0x38, 0x78, 0x34, 0x28, 0x5c, 0x00, 0x00,
		// 2 medium size producer
		4, 0x00, 0x00, 0x08, 0x00, 0x10, 0x18, 0x38, 0x10, 1, 0x2e, 0x00, 0x62, 0x76, 0x06, 0x40, 0x00, 0x06,
		// 3 medium size producer
		0, 0x04, 0x00, 0x50, 0x28, 0x19, 0x8f, 0x08, 0x28, 1, 0x08, 0x00, 0x02, 0x40, 0x24, 0x00, 0x00, 0x00,
		// 4 small producer
		3, 0x00, 0x00, 0x3c, 0x00, 0x5e, 0x76, 0x24, 0x3a, 10, 0x00, 0x00, 0x00, 0x00, 0x20, 0x08, 0x08, 0x40,
		// 5 small producer
		0, 0x58, 0x10, 0x36, 0x6a, 0x7a, 0xdc, 0x10, 0x3c, 0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		// 6 worm/snake-like
		14, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 1, 0x00, 0x00, 0x40, 0x00, 0xec, 0xa0, 0x1b, 0x3e,
		// 7 bilateral no legs/tentacles
		4, 0x00, 0x00, 0x2f, 0x06, 0x7f, 0x7a, 0xfc, 0xfe, 12, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00,
		// 8 bilateral monopedal
		0, 0x54, 0x38, 0x10, 0x7c, 0x10, 0x08, 0x18, 0x20, 3, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		// 9 bilateral bipedal
		11, 0x14, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 1, 0x2a, 0x1c, 0x1c, 0x1c, 0x14, 0x1c, 0x36, 0x14,
		// 10 bilateral tri/quadrapedal
		0, 0x31, 0x12, 0xf4, 0xc9, 0x14, 0x2a, 0x14, 0x02, 4, 0x00, 0x00, 0x0a, 0x14, 0x0a, 0x14, 0x22, 0x10,
		// 11 bilateral multipedal
		4, 0x00, 0x00, 0x00, 0x00, 0x54, 0x38, 0xaa, 0xfe, 12, 0x00, 0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00,
		// 12 radial no legs/tentacles
		0, 0x38, 0x00, 0x8a, 0x44, 0x8a, 0x86, 0x38, 0x44, 3, 0x00, 0x00, 0x74, 0x38, 0x74, 0x78, 0x00, 0x38,
		// 13 radial monopedal
		0, 0x44, 0x38, 0x44, 0x44, 0x10, 0x38, 0x10, 0x20, 3, 0x38, 0x00, 0x38, 0x38, 0x00, 0x00, 0x00, 0x00,
		// 14 radial multipedal
		0, 0x44, 0x38, 0x44, 0x44, 0x54, 0x38, 0xa4, 0x92, 1, 0x38, 0x00, 0x38, 0x38, 0x00, 0x00, 0x00, 0x00,
		// 15 amorphous
		12, 0x00, 0x00, 0x08, 0x00, 0x7c, 0x2c, 0xfe, 0x7e, 0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		// 16 amorphous multipedal
		0, 0x08, 0x00, 0x24, 0x00, 0x42, 0x08, 0x00, 0x00, 4, 0x10, 0x00, 0x5a, 0x3c, 0xbd, 0x76, 0xaa, 0x54,
		// 17 flying lifeform
		0, 0x00, 0x00, 0x99, 0x66, 0x00, 0x18, 0x00, 0x00, 0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		// 18 floating lifeform
		0, 0x68, 0x30, 0x30, 0x78, 0x58, 0x84, 0x74, 0x82, 0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		// 19 dead lifeform
		4, 0x42, 0x81, 0x18, 0x24, 0x24, 0x18, 0x81, 0x42, 0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		// 20 specimen
		0, 0x00, 0x00, 0x24, 0x60, 0x38, 0x38, 0x00, 0x28, 0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		// 21 mineral
		0, 0x6e, 0x00, 0xaa, 0xc6, 0x28, 0x10, 0x82, 0x44, 0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		// 22 artifact
		0, 0x00, 0x00, 0x08, 0x00, 0x08, 0x1c, 0x00, 0x00, 0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		// 23 recent ruins
		0, 0x68, 0x78, 0xee, 0xfe, 0xd5, 0xca, 0x7f, 0xd9, 12, 0x10, 0x00, 0x10, 0x00, 0x2a, 0x34, 0x00, 0x26,
		// 24 ancient ruins
		1, 0x18, 0x00, 0x70, 0x30, 0xe0, 0xe4, 0x00, 0x44, 12, 0x00, 0x00, 0x0c, 0x0c, 0x1e, 0x1a, 0x3f, 0x3b,
		// 25 ship
		0, 0x00, 0x00, 0x3c, 0x03, 0x00, 0x1c, 0x00, 0x03, 1, 0x07, 0x00, 0xc0, 0x5c, 0x5f, 0xe0, 0x00, 0x04,
		// 26 terrain vehicle
		0, 0x18, 0x00, 0x4a, 0x24, 0xd5, 0x7e, 0x7e, 0xab, 11, 0x00, 0x00, 0x34, 0x18, 0x2a, 0x00, 0x00, 0x54
	};

	// the 16 EGA colours (the adapter's own palette: the shipped game never sets the EGA palette, its only palette writes being the Tandy 1000's in GAME-OV
	// SET.COLORS)
	static readonly Color32[] s_egaColours =
	{
		new Color32( 0, 0, 0, 255 ), new Color32( 0, 0, 170, 255 ), new Color32( 0, 170, 0, 255 ), new Color32( 0, 170, 170, 255 ),
		new Color32( 170, 0, 0, 255 ), new Color32( 170, 0, 170, 255 ), new Color32( 170, 85, 0, 255 ), new Color32( 170, 170, 170, 255 ),
		new Color32( 85, 85, 85, 255 ), new Color32( 85, 85, 255, 255 ), new Color32( 85, 255, 85, 255 ), new Color32( 85, 255, 255, 255 ),
		new Color32( 255, 85, 85, 255 ), new Color32( 255, 85, 255, 255 ), new Color32( 255, 255, 85, 255 ), new Color32( 255, 255, 255, 255 )
	};

	// the pictures as textures and sprites, made the first time they are needed and kept for the session (there are 27 of them, 8 by 8 pixels)
	static Texture2D[] s_textures;
	static Sprite[] s_sprites;

	// the name of an icon ("" for no such icon)
	public static string GetName( int icon )
	{
		return ( ( icon >= 0 ) && ( icon < c_iconCount ) ) ? s_names[ icon ] : "";
	}

	// the picture of an icon, on the key's light grey (null for no such icon)
	public static Texture2D GetTexture( int icon )
	{
		if ( ( icon < 0 ) || ( icon >= c_iconCount ) )
		{
			return null;
		}

		if ( s_textures == null )
		{
			s_textures = new Texture2D[ c_iconCount ];
		}

		if ( s_textures[ icon ] == null )
		{
			s_textures[ icon ] = MakeTexture( icon );
		}

		return s_textures[ icon ];
	}

	// the picture of an icon as a sprite of one world unit (null for no such icon)
	public static Sprite GetSprite( int icon )
	{
		var texture = GetTexture( icon );

		if ( texture == null )
		{
			return null;
		}

		if ( s_sprites == null )
		{
			s_sprites = new Sprite[ c_iconCount ];
		}

		if ( s_sprites[ icon ] == null )
		{
			s_sprites[ icon ] = Sprite.Create( texture, new Rect( 0.0f, 0.0f, c_size, c_size ), new Vector2( 0.5f, 0.5f ), c_size );
			s_sprites[ icon ].name = "Icon " + GetName( icon );
		}

		return s_sprites[ icon ];
	}

	// the colour of a picture's pixel, its column from the left and its row from the top - the light grey where neither layer draws
	public static Color32 GetPixel( int icon, int column, int row )
	{
		var colour = s_egaColours[ c_backgroundColour ];

		if ( ( icon < 0 ) || ( icon >= c_iconCount ) || ( column < 0 ) || ( column >= c_size ) || ( row < 0 ) || ( row >= c_size ) )
		{
			return colour;
		}

		for ( var layer = 0; layer < 2; layer++ )
		{
			var start = icon * 18 + layer * 9;

			if ( ( ( s_pictures[ start + 1 + row ] >> ( 7 - column ) ) & 1 ) != 0 )
			{
				colour = s_egaColours[ s_pictures[ start ] & 15 ];
			}
		}

		return colour;
	}

	// draws a picture into a texture (Unity's rows go from the bottom up)
	static Texture2D MakeTexture( int icon )
	{
		var texture = new Texture2D( c_size, c_size, TextureFormat.RGBA32, false )
		{
			filterMode = FilterMode.Point,
			wrapMode = TextureWrapMode.Clamp,
			name = "Icon " + GetName( icon )
		};

		var pixels = new Color32[ c_size * c_size ];

		for ( var row = 0; row < c_size; row++ )
		{
			for ( var column = 0; column < c_size; column++ )
			{
				pixels[ ( c_size - 1 - row ) * c_size + column ] = GetPixel( icon, column, row );
			}
		}

		texture.SetPixels32( pixels );
		texture.Apply( false, true );

		return texture;
	}

	// the icon of a lifeform on the ground: a dead one's, a flying or floating one's, or its body plan's (the first of a producer's two pictures)
	public static int GetLifeformIcon( TerrainLifeform lifeform )
	{
		if ( lifeform == null )
		{
			return c_deadLifeform;
		}

		if ( lifeform.m_status == TerrainLifeform.Status.Dead )
		{
			return c_deadLifeform;
		}

		return GetSpeciesIcon( lifeform.m_species );
	}

	// the icon of a species: a flying or floating one's, else the one of its record in the shipped game's lifeforms, else its body plan's (the first of a producer's
	// two pictures)
	public static int GetSpeciesIcon( GD_Species species )
	{
		if ( species == null )
		{
			return c_snakelike;
		}

		if ( species.m_canFly )
		{
			return c_flyingLifeform;
		}

		if ( species.m_canFloat )
		{
			return c_floatingLifeform;
		}

		// the shipped game's own picture of the creature (its records' icons are the producers and the body plans, 0 to 16)
		if ( species.m_record != null )
		{
			return Mathf.Clamp( species.m_record.m_icon, c_largeProducer, c_flyingLifeform - 1 );
		}

		switch ( species.m_bodyPlan )
		{
			case GD_Species.BodyPlan.LeavedLarge:
				return c_largeProducer;

			case GD_Species.BodyPlan.LeavedMedium:
				return c_mediumProducer;

			case GD_Species.BodyPlan.LeavedSmall:
				return c_smallProducer;
		}

		// the animals' body plans are the pictures 6 to 16, in the same order
		return c_snakelike + Mathf.Clamp( (int) species.m_bodyPlan - (int) GD_Species.BodyPlan.Snakelike, 0, (int) GD_Species.BodyPlan.AmorphousLegs - (int) GD_Species.BodyPlan.Snakelike );
	}

	// the icon of a ruin
	public static int GetRuinIcon( TerrainRuin ruin )
	{
		return ( ( ruin != null ) && ruin.m_isAncient ) ? c_ancientRuins : c_recentRuins;
	}
}

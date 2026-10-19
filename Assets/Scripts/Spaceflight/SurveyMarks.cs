
using System.Collections.Generic;
using UnityEngine;

// draws the marks of the science officer's survey (PlanetSurvey) and the ancient ruins the Red Cylinder finds into a texture laid over a map of the planet:
// a mineral field is a cross and a fixed ancient ruin a box, each with a dark edge so that it reads on any ground, a region holding a random ancient ruin a
// light tint (a planet of ruins has hundreds of them: decided 2026-10-19) and a latitude band with life a tint in the life colour, stronger the more crowded
// it is (the shipped game places a block's lifeforms only as the terrain vehicle comes into it: decided 2026-10-19). The texture covers the planet's map as
// the landing map shows it: east-west -180 to 180 across, north-south -90 to 90 up (decided 2026-10-17, ROADMAP D6)
public static class SurveyMarks
{
	// the size of the texture (twice the landing map's 448 by 224, so that the marks stay sharp)
	public const int c_width = 896;
	public const int c_height = 448;

	// the sizes of the marks, in pixels of the texture: the half length of a cross's arms (the larger the field, the longer, up to the most), the half size
	// of a box, the width of their lines, and the width of their dark edge
	const float c_fewestArm = 6.0f;
	const float c_mostArm = 11.0f;
	public const float c_boxHalfSize = 11.0f;
	const float c_lineWidth = 2.5f;
	const float c_edgeWidth = 2.0f;

	// how opaque the dark edge is, and the tint of a region that holds a random ancient ruin
	const float c_edgeAlpha = 0.85f;
	const float c_regionTintAlpha = 0.3f;

	// how opaque the tint of a band with life is, from the least crowded region to the most (the shipped game's crowding, up to 256)
	const float c_leastLifeTintAlpha = 0.12f;
	const float c_mostLifeTintAlpha = 0.4f;
	const float c_mostCrowding = 256.0f;

	// the pixels drawn into (kept, so that a new drawing does not make garbage)
	static Color32[] s_pixels;

	// the marks of the planet the ship orbits, shared by the landing map and the sensor picture, and what they were last drawn for (they are drawn again
	// only when one of these changes)
	static Texture2D s_texture;
	static PlanetSurvey s_drawnSurvey;
	static int s_drawnPlanetId = -1;
	static int s_drawnRuinCount = -1;
	static int s_drawnRegionCount = -1;
	static UIKitTheme s_drawnTheme;

	/// <summary>
	/// The marks of the planet the ship orbits: the science officer's survey of it (none if the survey kept is of another planet) and its ancient ruins
	/// with the Red Cylinder aboard, in the colours drawn over the views in the scheme in use (the planet's ground is dark in either mode). Null if there
	/// is nothing to mark. The texture is shared and kept: do not change or destroy it.
	/// </summary>
	public static Texture2D GetTexture()
	{
		var controller = SpaceflightController.m_instance;

		if ( ( controller == null ) || ( DataController.m_instance == null ) )
		{
			return null;
		}

		var planetId = DataController.m_instance.m_playerData.m_general.m_currentPlanetId;

		// (the survey kept is of the planet analysed last, which may be another)
		var survey = ( ( controller.m_planetSurvey != null ) && ( controller.m_planetSurvey.m_planetId == planetId ) ) ? controller.m_planetSurvey : null;
		var ruins = RedCylinder.GetAncientRuinPlaces();
		var regions = RedCylinder.GetAncientRuinRegions();

		if ( ( ( survey == null ) || ( ( survey.m_marks.Count == 0 ) && ( survey.m_lifeBands.Count == 0 ) ) ) && ( ruins.Count == 0 ) && ( regions.Count == 0 ) )
		{
			return null;
		}

		var theme = UIKitTheme.GetViewTheme();

		// (a destroyed texture compares equal to null)
		if ( s_texture == null )
		{
			// mipmapped, since both pictures show it smaller than it is; it goes round the planet east to west, and stops at the poles
			s_texture = new Texture2D( c_width, c_height, TextureFormat.RGBA32, true );

			s_texture.name = "Survey Marks";
			s_texture.wrapModeU = TextureWrapMode.Repeat;
			s_texture.wrapModeV = TextureWrapMode.Clamp;
			s_texture.filterMode = FilterMode.Trilinear;

			s_drawnTheme = null;
		}
		else if ( ( survey == s_drawnSurvey ) && ( planetId == s_drawnPlanetId ) && ( ruins.Count == s_drawnRuinCount ) && ( regions.Count == s_drawnRegionCount ) && ( theme == s_drawnTheme ) )
		{
			return s_texture;
		}

		Draw( s_texture, survey, ruins, theme, regions );

		s_drawnSurvey = survey;
		s_drawnPlanetId = planetId;
		s_drawnRuinCount = ruins.Count;
		s_drawnRegionCount = regions.Count;
		s_drawnTheme = theme;

		return s_texture;
	}

	/// <summary>
	/// Draws the survey's marks (none if the survey is null), the ancient ruins (east-west and north-south, in degrees) and the regions that hold a random
	/// ancient ruin (RandomRuinRegions, none if null) into the texture, in the colours of this theme (the minerals in its armor colour, the bands with life
	/// in its fuel colour, the ruins and the regions' tint in its danger colour).
	/// </summary>
	public static void Draw( Texture2D texture, PlanetSurvey survey, List<Vector2> ruins, UIKitTheme theme, List<int> ancientRegions = null )
	{
		if ( ( texture == null ) || ( theme == null ) )
		{
			return;
		}

		var width = texture.width;
		var height = texture.height;

		if ( ( s_pixels == null ) || ( s_pixels.Length != width * height ) )
		{
			s_pixels = new Color32[ width * height ];
		}

		System.Array.Clear( s_pixels, 0, s_pixels.Length );

		var edge = theme.m_backgroundApp;

		edge.a = c_edgeAlpha;

		// the bands with life first, under everything
		if ( survey != null )
		{
			foreach ( var lifeBand in survey.m_lifeBands )
			{
				TintBand( width, height, lifeBand.m_southEdge, lifeBand.m_northEdge, theme.m_statusFuel, Mathf.Lerp( c_leastLifeTintAlpha, c_mostLifeTintAlpha, Mathf.Clamp01( lifeBand.m_crowding / c_mostCrowding ) ) );
			}
		}

		// then the regions of the random ancient ruins, under every mark
		if ( ancientRegions != null )
		{
			foreach ( var region in ancientRegions )
			{
				TintRegion( width, height, region, theme.m_statusDanger );
			}
		}

		if ( survey != null )
		{
			// the largest field (the crosses are as long as their fields are large, next to it)
			var largestField = 1;

			foreach ( var mark in survey.m_marks )
			{
				if ( mark.m_kind == PlanetSurvey.Kind.Minerals )
				{
					largestField = Mathf.Max( largestField, mark.m_size );
				}
			}

			// the mineral fields
			foreach ( var mark in survey.m_marks )
			{
				if ( mark.m_kind == PlanetSurvey.Kind.Minerals )
				{
					var arm = Mathf.Lerp( c_fewestArm, c_mostArm, Mathf.Clamp01( mark.m_size / (float) largestField ) );

					DrawCross( width, height, mark.m_eastWest, mark.m_northSouth, arm, edge, theme.m_statusArmor );
				}
			}
		}

		// the ancient ruins over everything
		if ( ruins != null )
		{
			foreach ( var ruin in ruins )
			{
				DrawBox( width, height, ruin.x, ruin.y, edge, theme.m_statusDanger );
			}
		}

		texture.SetPixels32( s_pixels );
		texture.Apply( texture.mipmapCount > 1 );
	}

	// where a place of the planet is on the texture (pixels from its left and bottom edges)
	public static Vector2 GetPixel( int width, int height, float eastWest, float northSouth )
	{
		return new Vector2( ( eastWest + 180.0f ) / 360.0f * width, ( northSouth + 90.0f ) / 180.0f * height );
	}

	// a cross (two lines, the dark edge first)
	static void DrawCross( int width, int height, float eastWest, float northSouth, float arm, Color edge, Color color )
	{
		var center = GetPixel( width, height, eastWest, northSouth );
		var a = new Vector2( arm, arm );
		var b = new Vector2( arm, -arm );

		DrawLine( width, height, center - a, center + a, c_lineWidth + c_edgeWidth * 2.0f, edge );
		DrawLine( width, height, center - b, center + b, c_lineWidth + c_edgeWidth * 2.0f, edge );
		DrawLine( width, height, center - a, center + a, c_lineWidth, color );
		DrawLine( width, height, center - b, center + b, c_lineWidth, color );
	}

	// a box (four lines)
	static void DrawBox( int width, int height, float eastWest, float northSouth, Color edge, Color color )
	{
		var center = GetPixel( width, height, eastWest, northSouth );
		var corners = new[] { new Vector2( -1, -1 ), new Vector2( 1, -1 ), new Vector2( 1, 1 ), new Vector2( -1, 1 ) };

		for ( var pass = 0; pass < 2; pass++ )
		{
			for ( var i = 0; i < 4; i++ )
			{
				var from = center + corners[ i ] * c_boxHalfSize;
				var to = center + corners[ ( i + 1 ) % 4 ] * c_boxHalfSize;

				if ( pass == 0 )
				{
					DrawLine( width, height, from, to, c_lineWidth + c_edgeWidth * 2.0f, edge );
				}
				else
				{
					DrawLine( width, height, from, to, c_lineWidth, color );
				}
			}
		}
	}

	// a line of this width from one point to the other (a dot if they are the same), smoothed at its edge, and drawn again a planet's width either way
	// where it crosses the map's east or west edge (east-west goes round the planet)
	static void DrawLine( int width, int height, Vector2 from, Vector2 to, float lineWidth, Color color )
	{
		var reach = lineWidth * 0.5f + 1.0f;
		var left = Mathf.Min( from.x, to.x ) - reach;
		var right = Mathf.Max( from.x, to.x ) + reach;

		for ( var wrap = -1; wrap <= 1; wrap++ )
		{
			var shift = wrap * width;

			if ( ( right + shift < 0.0f ) || ( left + shift >= width ) )
			{
				continue;
			}

			var shiftedFrom = new Vector2( from.x + shift, from.y );
			var shiftedTo = new Vector2( to.x + shift, to.y );

			var minimumX = Mathf.Max( 0, Mathf.FloorToInt( left + shift ) );
			var maximumX = Mathf.Min( width - 1, Mathf.CeilToInt( right + shift ) );
			var minimumY = Mathf.Max( 0, Mathf.FloorToInt( Mathf.Min( from.y, to.y ) - reach ) );
			var maximumY = Mathf.Min( height - 1, Mathf.CeilToInt( Mathf.Max( from.y, to.y ) + reach ) );

			for ( var y = minimumY; y <= maximumY; y++ )
			{
				for ( var x = minimumX; x <= maximumX; x++ )
				{
					// how much of this pixel the line covers (its middle against the line's distance, softened over one pixel)
					var distance = DistanceToSegment( new Vector2( x + 0.5f, y + 0.5f ), shiftedFrom, shiftedTo );
					var coverage = Mathf.Clamp01( lineWidth * 0.5f + 0.5f - distance );

					if ( coverage > 0.0f )
					{
						Blend( y * width + x, color, coverage );
					}
				}
			}
		}
	}

	// how far a point is from a line between two points
	static float DistanceToSegment( Vector2 point, Vector2 from, Vector2 to )
	{
		var along = to - from;
		var lengthSquared = along.sqrMagnitude;

		if ( lengthSquared < 0.0001f )
		{
			return Vector2.Distance( point, from );
		}

		var t = Mathf.Clamp01( Vector2.Dot( point - from, along ) / lengthSquared );

		return Vector2.Distance( point, from + along * t );
	}

	// lays the light tint over a region of the map (its pixels whose middles lie in it)
	static void TintRegion( int width, int height, int region, Color color )
	{
		RandomRuinRegions.GetRegionDegrees( region, out var west, out var east, out var south, out var north );

		var low = GetPixel( width, height, west, south );
		var high = GetPixel( width, height, east, north );

		for ( var y = Mathf.Max( 0, Mathf.CeilToInt( low.y - 0.5f ) ); y < Mathf.Min( height, Mathf.CeilToInt( high.y - 0.5f ) ); y++ )
		{
			for ( var x = Mathf.Max( 0, Mathf.CeilToInt( low.x - 0.5f ) ); x < Mathf.Min( width, Mathf.CeilToInt( high.x - 0.5f ) ); x++ )
			{
				Blend( y * width + x, color, c_regionTintAlpha );
			}
		}
	}

	// lays a tint over a band of latitude all round the planet (its pixels whose middles lie in it)
	static void TintBand( int width, int height, float southEdge, float northEdge, Color color, float alpha )
	{
		var low = GetPixel( width, height, -180.0f, southEdge );
		var high = GetPixel( width, height, 180.0f, northEdge );

		for ( var y = Mathf.Max( 0, Mathf.CeilToInt( low.y - 0.5f ) ); y < Mathf.Min( height, Mathf.CeilToInt( high.y - 0.5f ) ); y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				Blend( y * width + x, color, alpha );
			}
		}
	}

	// lays a colour over a pixel ("over", with straight alpha, as the UI draws the texture)
	static void Blend( int index, Color color, float coverage )
	{
		Color below = s_pixels[ index ];

		var alpha = color.a * coverage;
		var outAlpha = alpha + below.a * ( 1.0f - alpha );

		if ( outAlpha <= 0.0f )
		{
			return;
		}

		var blended = ( color * alpha + below * ( below.a * ( 1.0f - alpha ) ) ) / outAlpha;

		blended.a = outAlpha;

		s_pixels[ index ] = blended;
	}
}

using UnityEngine;

// how much detail the ground is drawn with while the terrain vehicle is out (since 2026-10-17, ROADMAP D7): Auto picks a level from the graphics card, and the
// player can set Low, Medium or High instead on the save panel's Colors screen. The terrain grid follows it (TerrainGrid.SetDetail, from Disembarked)
public static class GroundDetail
{
	// what the player can pick
	public enum Setting
	{
		Auto, Low, Medium, High
	}

	// what the ground is drawn with
	public enum Level
	{
		Low, Medium, High
	}

	// where the player's choice is kept: a setting of this computer, not of a saved game
	public const string c_settingKey = "Ground Detail";

	// what Auto takes: High with 4 GB of graphics memory or more, Medium with 2 GB, Low with less or without a graphics device (a headless run)
	public const int c_highMemoryMB = 4096;
	public const int c_mediumMemoryMB = 2048;

	// the terrain grid at each level: vertices along each ring's side and rings. 257 vertices over the 256 units of the inner ring are a vertex a unit, and 129
	// one every two; both divide the 8 units Disembarked moves the grid by, so the ground does not swim as the vehicle drives. High adds a fourth ring, twice as
	// wide, to carry the detailed ground farther out
	static readonly int[] c_gridDetail = { 129, 257, 257 };
	static readonly int[] c_gridRings = { 3, 3, 4 };

	// how strongly the ground is darkened where it curves in and lightened where it curves out (SF - TerrainGrid.cginc): none at Low
	static readonly float[] c_cavityStrength = { 0.0f, 0.6f, 0.9f };

	// the shadows while the terrain vehicle is out: hard, at medium resolution, in 2 cascades out to 300 units at Low (for the cards Auto puts there); the
	// project's own at Medium; and at very high resolution at High (Disembarked puts the project's back when the vehicle comes in)
	static readonly ShadowQuality[] c_shadows = { ShadowQuality.HardOnly, ShadowQuality.All, ShadowQuality.All };
	static readonly ShadowResolution[] c_shadowResolution = { ShadowResolution.Medium, ShadowResolution.High, ShadowResolution.VeryHigh };
	static readonly int[] c_shadowCascades = { 2, 4, 4 };
	static readonly float[] c_shadowDistance = { 300.0f, 768.0f, 768.0f };

	// the setting in use (read the first time it is asked for)
	static bool s_settingKnown;
	static Setting s_setting;

	// called when the level changes, so that the ground on screen takes it at once
	public static event System.Action Changed;

	// returns the player's setting (Auto if there is none or it cannot be read)
	public static Setting GetSetting()
	{
		if ( !s_settingKnown )
		{
			s_setting = Setting.Auto;

			try
			{
				var name = PlayerPrefs.GetString( c_settingKey, Setting.Auto.ToString() );

				foreach ( Setting setting in System.Enum.GetValues( typeof( Setting ) ) )
				{
					if ( name == setting.ToString() )
					{
						s_setting = setting;
					}
				}
			}
			catch ( System.Exception exception )
			{
				Debug.LogWarning( "The ground detail could not be read: " + exception.Message );
			}

			s_settingKnown = true;
		}

		return s_setting;
	}

	// returns the level the ground is drawn with: the player's, or the graphics card's for Auto
	public static Level GetLevel()
	{
		switch ( GetSetting() )
		{
			case Setting.Low:
				return Level.Low;

			case Setting.Medium:
				return Level.Medium;

			case Setting.High:
				return Level.High;

			default:
				return DetectLevel();
		}
	}

	// the level Auto takes on this computer, by its graphics memory
	public static Level DetectLevel()
	{
		if ( SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null )
		{
			return Level.Low;
		}

		var memory = SystemInfo.graphicsMemorySize;

		if ( memory >= c_highMemoryMB )
		{
			return Level.High;
		}

		return ( memory >= c_mediumMemoryMB ) ? Level.Medium : Level.Low;
	}

	// the terrain grid at the level in use: vertices along each ring's side, and rings
	public static int GetGridDetail()
	{
		return c_gridDetail[ (int) GetLevel() ];
	}

	public static int GetGridRings()
	{
		return c_gridRings[ (int) GetLevel() ];
	}

	// how strongly the ground's curves are shaded at the level in use
	public static float GetCavityStrength()
	{
		return c_cavityStrength[ (int) GetLevel() ];
	}

	// sets the shadows for the ground at the level in use
	public static void ApplyShadows()
	{
		var level = (int) GetLevel();

		QualitySettings.shadows = c_shadows[ level ];
		QualitySettings.shadowResolution = c_shadowResolution[ level ];
		QualitySettings.shadowCascades = c_shadowCascades[ level ];
		QualitySettings.shadowDistance = c_shadowDistance[ level ];
	}

	// changes the setting and lets the ground on screen know (remember false leaves the player's setting as it is, for a test)
	public static void SetSetting( Setting setting, bool remember = true )
	{
		var previousLevel = GetLevel();
		var previousSetting = GetSetting();

		if ( remember )
		{
			try
			{
				PlayerPrefs.SetString( c_settingKey, setting.ToString() );
				PlayerPrefs.Save();
			}
			catch ( System.Exception exception )
			{
				Debug.LogWarning( "The ground detail could not be saved: " + exception.Message );
			}
		}

		s_setting = setting;
		s_settingKnown = true;

		if ( ( setting != previousSetting ) || ( GetLevel() != previousLevel ) )
		{
			Changed?.Invoke();
		}
	}
}

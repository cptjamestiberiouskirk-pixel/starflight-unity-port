
using UnityEngine;

using System.Collections.Generic;

public class TerrainGridPopulator : MonoBehaviour
{
	const float c_minDistanceBetweenObjects = 8.0f;

	const int c_spawnListWidth = 32;
	const int c_spawnListHeight = 16;

	static PlanetGenerator m_planetGenerator;

	static List<Vector3>[,] m_spawnLists;

	// delegate for object spawned callback
	public delegate void ObjectSpawnedCallback( GameObject spawnedObject, int templateIndex );

	public static void ResetSpawnLists( PlanetGenerator planetGenerator )
	{
		// remember the planet generator
		m_planetGenerator = planetGenerator;

		// create the spawn list (stupid unity physics wont work here because they dont update colliders until fixedupate and there is no way to force an update)
		m_spawnLists = new List<Vector3>[ c_spawnListHeight, c_spawnListWidth ];

		for ( var y = 0; y < c_spawnListHeight; y++ )
		{
			for ( var x = 0; x < c_spawnListWidth; x++ )
			{
				m_spawnLists[ y, x ] = new List<Vector3>();
			}
		}
	}

	// the planet generator of the planet the objects are placed on (null once the planet has been let go of)
	protected static PlanetGenerator GetPlanetGenerator()
	{
		return m_planetGenerator;
	}

	// let go of the planet generator and the spawn lists (they are statics, so they would stay in memory after the spaceflight scene is gone)
	public static void ForgetPlanet()
	{
		m_planetGenerator = null;
		m_spawnLists = null;
	}

	// places one object at a given place on the planet's map, with no random numbers: on the surface, turned by yaw degrees, and in the spawn list,
	// so that nothing placed after it lands on it
	protected GameObject PlaceObjectAt( GameObject template, float mapX, float mapY, float elevationScale, float yaw )
	{
		var normal = m_planetGenerator.GetBilinearSmoothedNormal( mapX, mapY, elevationScale * 0.125f );

		var position = Tools.MapToWorldCoordinates( mapX, mapY, m_planetGenerator.m_textureMapWidth, m_planetGenerator.m_textureMapHeight );

		position.y = m_planetGenerator.GetBilinearSmoothedElevation( mapX, mapY ) * elevationScale;

		var rotation = Quaternion.FromToRotation( Vector3.up, normal ) * Quaternion.Euler( 0.0f, yaw, 0.0f ) * template.transform.localRotation;

		var clonedObject = Instantiate( template, position, rotation, transform );

		AddToSpawnList( position );

		return clonedObject;
	}

	// populate the planet
	protected void Initialize( float elevationScale, GameObject[] templates, int numObjects, int randomSeed, bool favorHigherElevations, float minScale, float maxScale, ObjectSpawnedCallback onObjectSpawned = null )
	{
		Debug.Log( "Placing " + numObjects + " objects under " + transform.name + " with using a random seed of " + randomSeed + "..." );

		// remove all of the current objects
		Tools.DestroyChildrenOf( gameObject );

		// if we don't want to place anything then stop here now
		if ( numObjects == 0 )
		{
			return;
		}

		// remember where the game's random numbers are, so that they can carry on from there when we are done
		var randomState = Random.state;

		// reset random number generator to a deterministic value (so objects are always in the same place for each planet)
		Random.InitState( randomSeed );

		// get the number of template objects we have to clone from
		var numTemplateObjects = templates.Length;

		// calculate the minimum elevation we'd like to place objects at
		var minimumElevation = ( m_planetGenerator.m_maximumElevation - m_planetGenerator.m_waterElevation ) * 0.05f + m_planetGenerator.m_waterElevation;

		// we'll keep track of the surface normal and position for this object
		var normal = Vector3.zero;
		var position = Vector3.zero;

		// start placing objects
		for ( var i = 0; i < numObjects; i++ )
		{
			// grab the template for the next object
			var template = templates[ i % numTemplateObjects ];

			// we'll figure out what values to use for these in the loop
			var mapX = 0.0f;
			var mapY = 0.0f;

			var elevation = 0.0f;

			// maximum of 10 tries to put this object somewhere decent
			var spotFound = false;

			for ( var j = 0; j < 10; j++ )
			{
				// pick a random place on the planet surface
				mapX = Random.Range( 0.0f, m_planetGenerator.m_textureMapWidth );
				mapY = Random.Range( m_planetGenerator.m_textureMapHeight * 0.125f, m_planetGenerator.m_textureMapHeight * 0.875f );

				// Debug.Log( "Object #" + ( i + 1 ) + ", trying mapX=" + mapX + ", mapY=" + mapY + "..." );

				// get the surface normal
				normal = m_planetGenerator.GetBilinearSmoothedNormal( mapX, mapY, elevationScale * 0.125f );

				// is the surface steep (more that 45 degree slope)?
				if ( normal.y < 0.707f )
				{
					// Debug.Log( "...too steep!" );

					// yes - let's not put it here
					continue;
				}

				// get the elevation
				elevation = m_planetGenerator.GetBilinearSmoothedElevation( mapX, mapY );

				// is the elevation is below the minimum elevation
				if ( elevation < minimumElevation )
				{
					// Debug.Log( "...below minimum elevation!" );

					// yes - let's not put it here
					continue;
				}

				// pick a random elevation somewhere between the minimum and the maximum elevation
				var randomElevation = Random.Range( minimumElevation, m_planetGenerator.m_maximumElevation );

				// do we want to favor the higher elevations?
				if ( favorHigherElevations )
				{
					// yes - are we below the random elevation?
					if ( elevation <= randomElevation )
					{
						// Debug.Log( "...below random elevation!" );

						// yes - let's not put it here
						continue;
					}
				}
				else
				{
					// no - are we above the random elevation?
					if ( elevation >= randomElevation )
					{
						// Debug.Log( "...above random elevation!" );

						// yes - let's not put it here
						continue;
					}
				}

				// calculate the position of this object in world coordinates
				position = Tools.MapToWorldCoordinates( mapX, mapY, m_planetGenerator.m_textureMapWidth, m_planetGenerator.m_textureMapHeight );

				position.y = elevation * elevationScale;

				// check if this object is too close to another object we've already placed

				if ( OverlapsSomething( position ) )
				{
					// Debug.Log( "...overlaps another object!" );

					// nope - let's not put it here
					continue;
				}

				// yay! let's put it here
				spotFound = true;
				break;
			}

			// did we find a good spot to put this object?
			if ( !spotFound )
			{
				// no - ok let's skip this one
				continue;
			}

			// give the object a random rotation and align it with the surface normal
			var rotation = Quaternion.FromToRotation( Vector3.up, normal ) * Quaternion.Euler( 0.0f, Random.Range( 0.0f, 360.0f ), 0.0f ) * template.transform.localRotation;

			// tell unity to clone an object from the selected template with the position and rotation we want it to have
			var clonedObject = Instantiate( template, position, rotation, transform );

			clonedObject.name = "Clone #" + ( i + 1 ) + " (from " + template.name + ")";

			// give the cloned object a random scale
			clonedObject.transform.localScale = Vector3.one * Random.Range( minScale, maxScale );

			// add object to the spawn list
			AddToSpawnList( position );

			// call the callback if one was provided
			if ( onObjectSpawned != null )
			{
				onObjectSpawned( clonedObject, i % numTemplateObjects );
			}
		}

		// give the game its random numbers back (the seed is the same every time for a planet, so without this everything
		// random that came after a landing - what the aliens do and say, for one - went the same way after every landing there)
		Random.state = randomState;
	}

	// ---- the same placing, worked out without placing anything (PlanetSurvey: the science officer's survey from orbit)

	// one object the placing would put on the planet: where, its template, and the volume a deposit's callback draws for it (0 for anything else)
	public struct ReplayedPlace
	{
		public Vector3 m_position;
		public int m_templateIndex;
		public int m_volume;
	}

	// makes empty spawn lists for a replay (a replay keeps its own, so that it never disturbs the surface's)
	public static List<Vector3>[,] MakeSpawnLists()
	{
		var spawnLists = new List<Vector3>[ c_spawnListHeight, c_spawnListWidth ];

		for ( var y = 0; y < c_spawnListHeight; y++ )
		{
			for ( var x = 0; x < c_spawnListWidth; x++ )
			{
				spawnLists[ y, x ] = new List<Vector3>();
			}
		}

		return spawnLists;
	}

	/// <summary>
	/// Works out where Initialize places its objects on a planet, without placing any: the same random numbers in the same order, so the same places
	/// (keep the two loops the same; the probe's orbitsurvey compares them with the objects on the surface). The objects placed before it go in the
	/// spawn lists, which this adds to. drawVolume is a deposit's callback (it draws one random number for each deposit placed); null for objects whose
	/// callback draws none. The game's random numbers are put back afterwards, as Initialize does.
	/// </summary>
	public static List<ReplayedPlace> ReplayPlaces( PlanetGenerator planetGenerator, List<Vector3>[,] spawnLists, float elevationScale, int numTemplates, int numObjects, int randomSeed, bool favorHigherElevations, float minScale, float maxScale, System.Func<int> drawVolume )
	{
		var places = new List<ReplayedPlace>();

		if ( ( numObjects <= 0 ) || ( numTemplates <= 0 ) || ( planetGenerator == null ) || ( planetGenerator.m_elevation == null ) )
		{
			return places;
		}

		var randomState = Random.state;

		Random.InitState( randomSeed );

		var minimumElevation = ( planetGenerator.m_maximumElevation - planetGenerator.m_waterElevation ) * 0.05f + planetGenerator.m_waterElevation;
		var position = Vector3.zero;

		for ( var i = 0; i < numObjects; i++ )
		{
			var spotFound = false;

			for ( var j = 0; j < 10; j++ )
			{
				var mapX = Random.Range( 0.0f, planetGenerator.m_textureMapWidth );
				var mapY = Random.Range( planetGenerator.m_textureMapHeight * 0.125f, planetGenerator.m_textureMapHeight * 0.875f );

				var normal = planetGenerator.GetBilinearSmoothedNormal( mapX, mapY, elevationScale * 0.125f );

				if ( normal.y < 0.707f )
				{
					continue;
				}

				var elevation = planetGenerator.GetBilinearSmoothedElevation( mapX, mapY );

				if ( elevation < minimumElevation )
				{
					continue;
				}

				var randomElevation = Random.Range( minimumElevation, planetGenerator.m_maximumElevation );

				if ( favorHigherElevations ? ( elevation <= randomElevation ) : ( elevation >= randomElevation ) )
				{
					continue;
				}

				position = Tools.MapToWorldCoordinates( mapX, mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

				position.y = elevation * elevationScale;

				if ( OverlapsSomething( planetGenerator, spawnLists, position ) )
				{
					continue;
				}

				spotFound = true;
				break;
			}

			if ( !spotFound )
			{
				continue;
			}

			// the rotation's and the scale's random numbers, as Initialize draws them, then the callback's
			Random.Range( 0.0f, 360.0f );
			Random.Range( minScale, maxScale );

			AddToSpawnList( planetGenerator, spawnLists, position );

			places.Add( new ReplayedPlace { m_position = position, m_templateIndex = i % numTemplates, m_volume = ( drawVolume != null ) ? drawVolume() : 0 } );
		}

		Random.state = randomState;

		return places;
	}

	// populate the planet with a callback for each spawned object
	protected void InitializeWithCallback( float elevationScale, GameObject[] templates, int numObjects, int randomSeed, bool favorHigherElevations, float minScale, float maxScale, ObjectSpawnedCallback callback )
	{
		Initialize( elevationScale, templates, numObjects, randomSeed, favorHigherElevations, minScale, maxScale, callback );
	}

	// figure out which of the spawn lists a position on the planet belongs to
	static void GetSpawnList( Vector3 position, out int listX, out int listY )
	{
		GetSpawnList( m_planetGenerator, position, out listX, out listY );
	}

	static void GetSpawnList( PlanetGenerator planetGenerator, Vector3 position, out int listX, out int listY )
	{
		Tools.WorldToMapCoordinates( position, out var mapX, out var mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

		listX = Mathf.FloorToInt( (float) mapX / (float) planetGenerator.m_textureMapWidth * (float) c_spawnListWidth );
		listY = Mathf.FloorToInt( (float) mapY / (float) planetGenerator.m_textureMapHeight * (float) c_spawnListHeight );

		// a position on the very edge of the map belongs to the last list, not to one past it (Random.Range can
		// return its upper limit, which is the width of the map, and that is one list too far)
		listX = Mathf.Clamp( listX, 0, c_spawnListWidth - 1 );
		listY = Mathf.Clamp( listY, 0, c_spawnListHeight - 1 );
	}

	static void AddToSpawnList( Vector3 position )
	{
		AddToSpawnList( m_planetGenerator, m_spawnLists, position );
	}

	static void AddToSpawnList( PlanetGenerator planetGenerator, List<Vector3>[,] spawnLists, Vector3 position )
	{
		GetSpawnList( planetGenerator, position, out var listX, out var listY );

		spawnLists[ listY, listX ].Add( position );
	}

	static bool OverlapsSomething( Vector3 position )
	{
		return OverlapsSomething( m_planetGenerator, m_spawnLists, position );
	}

	static bool OverlapsSomething( PlanetGenerator planetGenerator, List<Vector3>[,] spawnLists, Vector3 position )
	{
		GetSpawnList( planetGenerator, position, out var listX, out var listY );

		for ( var y = -1; y <= 1; y++ )
		{
			var listYOffset = listY + y;

			// there are no lists above the top row or below the bottom row (this tested listY, which is always inside)
			if ( ( listYOffset < 0 ) || ( listYOffset >= c_spawnListHeight ) )
			{
				continue;
			}

			for ( var x = -1; x <= 1; x++ )
			{
				var listXOffset = ( listX + x + c_spawnListWidth ) & ( c_spawnListWidth - 1 );

				foreach ( var otherPosition in spawnLists[ listYOffset, listXOffset ] )
				{
					var distance = Vector3.Magnitude( otherPosition - position );

					if ( distance < c_minDistanceBetweenObjects )
					{
						return true;
					}
				}
			}
		}

		return false;
	}
}

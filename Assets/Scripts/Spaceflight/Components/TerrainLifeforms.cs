using UnityEngine;

// places the lifeforms of a planet: some of each of its species (GD_Species.ForPlanet), where the manual (page 21) says they live - "lifeforms are not distributed
// evenly over the planet's surface, but tend to clump in the lower elevations near the equator or other temperate areas" - never on liquid, mud or lava
// (planet.txt) - with the planet's random numbers, after every other populator, so nothing else moves; they are placed again on every visit (the notes:
// the lifeforms of an encounter area are made as it is entered) - how many of each species and where exactly are the port's
public class TerrainLifeforms : TerrainGridPopulator
{
	// how many of a species are placed
	const int c_fewestOfASpecies = 3;
	const int c_mostOfASpecies = 8;

	// how many places to try for each lifeform (the lowest is taken)
	const int c_tries = 6;

	// the size of the smallest lifeform's marker, in world units (and as much again for each step of size)
	const float c_markerScale = 1.5f;

	// place the lifeforms of this planet
	public void Initialize( PlanetGenerator planetGenerator, float elevationScale, int randomSeed )
	{
		// remove the lifeforms of the last planet
		Tools.DestroyChildrenOf( gameObject );

		var speciesList = GD_Species.ForPlanet( planetGenerator.GetPlanet() );

		if ( speciesList.Count == 0 )
		{
			return;
		}

		// remember where the game's random numbers are, so that they can carry on from there when we are done (as the other populators do)
		var randomState = Random.state;

		Random.InitState( randomSeed );

		var minimumElevation = ( planetGenerator.m_maximumElevation - planetGenerator.m_waterElevation ) * 0.05f + planetGenerator.m_waterElevation;

		foreach ( var species in speciesList )
		{
			var count = Random.Range( c_fewestOfASpecies, c_mostOfASpecies + 1 );

			for ( var i = 0; i < count; i++ )
			{
				// the lowest dry place of a few near the equator
				var found = false;
				var bestX = 0.0f;
				var bestY = 0.0f;
				var bestElevation = float.MaxValue;

				for ( var j = 0; j < c_tries; j++ )
				{
					var mapX = Random.Range( 0.0f, planetGenerator.m_textureMapWidth );
					var mapY = Random.Range( planetGenerator.m_textureMapHeight * 0.35f, planetGenerator.m_textureMapHeight * 0.65f );
					var elevation = planetGenerator.GetBilinearSmoothedElevation( mapX, mapY );

					if ( ( elevation >= minimumElevation ) && ( elevation < bestElevation ) )
					{
						found = true;
						bestX = mapX;
						bestY = mapY;
						bestElevation = elevation;
					}
				}

				if ( found )
				{
					PlaceLifeform( species, bestX, bestY, elevationScale );
				}
			}
		}

		// give the game its random numbers back
		Random.state = randomState;
	}

	// a lifeform at this place: a plain shape in its species' colour stands in for it until there are models (a pillar for a plant, a ball for a radial animal,
	// a box for an amorphous one, and a capsule for a bilateral one), as large as its size
	void PlaceLifeform( GD_Species species, float mapX, float mapY, float elevationScale )
	{
		var planetGenerator = GetPlanetGenerator();

		PrimitiveType shape;

		if ( species.IsPlant() )
		{
			shape = PrimitiveType.Cylinder;
		}
		else if ( ( species.m_bodyPlan >= GD_Species.BodyPlan.RadialNoLegs ) && ( species.m_bodyPlan <= GD_Species.BodyPlan.RadialManyLegs ) )
		{
			shape = PrimitiveType.Sphere;
		}
		else if ( species.m_bodyPlan >= GD_Species.BodyPlan.AmorphousNoLegs )
		{
			shape = PrimitiveType.Cube;
		}
		else
		{
			shape = PrimitiveType.Capsule;
		}

		var lifeformObject = GameObject.CreatePrimitive( shape );

		// no collider: nothing collides with the objects on the ground
		var collider = lifeformObject.GetComponent<Collider>();

		if ( collider != null )
		{
			Destroy( collider );
		}

		var position = Tools.MapToWorldCoordinates( mapX, mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );
		var size = c_markerScale * ( 1.0f + species.m_size * 0.5f );

		position.y = planetGenerator.GetBilinearSmoothedElevation( mapX, mapY ) * elevationScale + size * 0.5f;

		lifeformObject.transform.SetParent( transform, false );
		lifeformObject.transform.position = position;
		lifeformObject.transform.localScale = Vector3.one * size;
		lifeformObject.name = "Lifeform " + species.m_index + " (" + species.GetBodyPlanText() + ")";

		var renderer = lifeformObject.GetComponent<Renderer>();

		if ( renderer != null )
		{
			renderer.material.color = new Color( species.m_red, species.m_green, species.m_blue );
		}

		lifeformObject.AddComponent<TerrainLifeform>().Initialize( species );
	}
}

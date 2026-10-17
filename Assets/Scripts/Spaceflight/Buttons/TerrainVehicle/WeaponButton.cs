// the terrain vehicle's tools: Mine (the mining laser, since 2026-10-17), Fire, Switch (the laser or the stunner) and Back (the manual, page 21: "Your Terrain
// Vehicle is equipped with a laser and a stunner. You can choose which one to fire through the Weapons option.")
public class WeaponButton : ShipButton
{
	public override string GetLabel()
	{
		return "Tools";
	}

	public override bool Execute()
	{
		var terrainVehicle = DataController.m_instance.m_playerData.m_terrainVehicle;

		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Mine cuts the nearest mineral deposit into the cargo hold. The " + ( terrainVehicle.m_stunnerMounted ? "stunner" : "laser" ) + " is mounted: Fire hits the nearest lifeform within its range.</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.TerrainVehicleWeapon );

		return false;
	}
}

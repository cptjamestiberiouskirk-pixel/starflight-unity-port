
// mounts the other of the terrain vehicle's two weapons (the manual, page 21: "Your Terrain Vehicle is equipped with a laser and a stunner. You can choose which one
// to fire through the Weapons option.")
public class TVSwitchWeaponButton : ShipButton
{
	public override string GetLabel()
	{
		return "Switch";
	}

	public override bool Execute()
	{
		var terrainVehicle = DataController.m_instance.m_playerData.m_terrainVehicle;

		terrainVehicle.m_stunnerMounted = !terrainVehicle.m_stunnerMounted;

		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>The " + ( terrainVehicle.m_stunnerMounted ? "stunner" : "laser" ) + " is mounted.</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}

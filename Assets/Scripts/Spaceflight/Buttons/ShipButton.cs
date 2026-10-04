
abstract public class ShipButton
{
	public virtual string GetLabel()
	{
		return "???";
	}

	public virtual bool Execute()
	{
		return false;
	}

	public virtual bool Update()
	{
		return false;
	}

	// formats a number of seconds as minutes and seconds for the message box (for example 1:05)
	protected static string FormatDuration( float seconds )
	{
		var wholeSeconds = UnityEngine.Mathf.CeilToInt( UnityEngine.Mathf.Max( 0.0f, seconds ) );

		return ( wholeSeconds / 60 ) + ":" + ( wholeSeconds % 60 ).ToString( "D2" );
	}
}

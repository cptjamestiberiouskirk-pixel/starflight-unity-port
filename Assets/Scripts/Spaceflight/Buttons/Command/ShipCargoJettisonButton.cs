using UnityEngine;
using UnityEngine.InputSystem;

// jettisons the marked item of the ship's cargo list, as the original's JETTISON (CAPTAIN WECDF, TOSS): an artifact or a specimen goes at once; for an element it
// first asks how much, as the original's KEY-ELEM-AMT ("JETTISON HOW MANY CUBIC METERS? ENTER VOLUME (0-20.0)"), starting at all of it: the number keys type the
// volume as the original's do, the stick steps it (up and down a cubic meter, left and right a tenth: the port's), and fire or Enter jettisons it (0 jettisons
// nothing)
public class ShipCargoJettisonButton : ShipButton
{
	// how long the stick is held before the volume keeps stepping, and how often it steps then
	const float c_firstRepeatSeconds = 0.5f;
	const float c_repeatSeconds = 0.1f;

	// the number keys, by the digit they type
	static readonly Key[] c_digitKeys = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };

	// true while it asks how much of an element to jettison, the volume (in tenths of a cubic meter) and the most there is, and what has been typed
	bool m_isAsking;
	int m_volume;
	int m_maximum;
	string m_typed = "";

	// true until the press that opened the question has been let go, the direction the stick was held in last frame, and when it steps again
	bool m_waitForRelease;
	int m_heldStep;
	float m_repeatTimer;

	public override string GetLabel()
	{
		return "Jettison";
	}

	public override bool Execute()
	{
		// an artifact or a specimen goes at once
		if ( !ShipCargoButton.MarkedItemIsElement( out var volume ) )
		{
			SoundController.m_instance.PlaySound( ShipCargoButton.JettisonMarkedItem( 0 ) ? SoundController.Sound.Deactivate : SoundController.Sound.Error );

			return false;
		}

		// an element: how much?
		m_isAsking = true;
		m_maximum = volume;
		m_volume = volume;
		m_typed = "";
		m_waitForRelease = true;
		m_heldStep = 0;
		m_repeatTimer = 0.0f;

		ShowQuestion();

		// the console's stick and fire button are the question's until it is answered
		return true;
	}

	public override bool Update()
	{
		if ( !m_isAsking )
		{
			return false;
		}

		var input = InputController.m_instance;

		// the press that opened the question is not its answer
		if ( m_waitForRelease )
		{
			if ( input.m_submit )
			{
				return true;
			}

			m_waitForRelease = false;
		}

		// the number keys type the volume, as the original's (a decimal point for the tenths, Backspace takes the last key back)
		var keyboard = Keyboard.current;

		if ( keyboard != null )
		{
			var typed = m_typed;

			for ( var digit = 0; digit < c_digitKeys.Length; digit++ )
			{
				if ( keyboard[ c_digitKeys[ digit ] ].wasPressedThisFrame && ( typed.Length < 6 ) )
				{
					typed += digit.ToString();
				}
			}

			if ( ( keyboard.periodKey.wasPressedThisFrame || keyboard.numpadPeriodKey.wasPressedThisFrame ) && !typed.Contains( "." ) )
			{
				typed += ".";
			}

			if ( keyboard.backspaceKey.wasPressedThisFrame && ( typed.Length > 0 ) )
			{
				typed = typed.Substring( 0, typed.Length - 1 );
			}

			if ( typed != m_typed )
			{
				m_typed = typed;
				m_volume = Mathf.Clamp( ParseVolume( m_typed ), 0, m_maximum );

				ShowQuestion();
			}
		}

		// the stick steps it: once when it is pushed, and on and on while it is held
		var step = input.m_north ? 10 : ( input.m_south ? -10 : ( input.m_east ? 1 : ( input.m_west ? -1 : 0 ) ) );

		if ( step != m_heldStep )
		{
			m_heldStep = step;
			m_repeatTimer = c_firstRepeatSeconds;

			if ( step != 0 )
			{
				StepVolume( step );
			}
		}
		else if ( step != 0 )
		{
			m_repeatTimer -= Time.deltaTime;

			if ( m_repeatTimer <= 0.0f )
			{
				m_repeatTimer = c_repeatSeconds;

				StepVolume( step );
			}
		}

		// fire or Enter: overboard it goes
		if ( input.m_submit )
		{
			m_isAsking = false;

			input.Debounce();

			SpaceflightController.m_instance.m_buttonController.ClearCurrentButton();

			SoundController.m_instance.PlaySound( ShipCargoButton.JettisonMarkedItem( m_volume ) ? SoundController.Sound.Deactivate : SoundController.Sound.Click );

			return true;
		}

		return true;
	}

	// changes the volume by this many tenths, within what there is
	void StepVolume( int tenths )
	{
		m_volume = Mathf.Clamp( m_volume + tenths, 0, m_maximum );
		m_typed = "";

		SoundController.m_instance.PlaySound( SoundController.Sound.Click );

		ShowQuestion();
	}

	// the question, in the original's words, with the volume as it stands
	void ShowQuestion()
	{
		var messages = SpaceflightController.m_instance.m_messages;

		messages.Clear();
		messages.AddText( "<color=white>Jettison how many cubic meters?\nEnter volume (0-" + Tools.VolumeToText( m_maximum ) + "): " + ( ( m_typed != "" ) ? m_typed : Tools.VolumeToText( m_volume ) ) + "</color>" );
	}

	// what the typed keys say, in tenths of a cubic meter ("5" is 50, "5.5" is 55, ".5" is 5; a second decimal is dropped)
	public static int ParseVolume( string typed )
	{
		if ( string.IsNullOrEmpty( typed ) )
		{
			return 0;
		}

		var parts = typed.Split( '.' );
		var whole = 0;

		if ( parts[ 0 ].Length > 0 )
		{
			int.TryParse( parts[ 0 ], out whole );
		}

		var tenths = ( ( parts.Length > 1 ) && ( parts[ 1 ].Length > 0 ) ) ? ( parts[ 1 ][ 0 ] - '0' ) : 0;

		return whole * 10 + Mathf.Clamp( tenths, 0, 9 );
	}
}

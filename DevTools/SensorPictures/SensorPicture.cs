using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// makes a sensor picture (background and mask, 1024 x 512) from a screenshot of the original game's sensor window
public static class SensorPicture
{
	const int c_canvasWidth = 1024;
	const int c_canvasHeight = 512;

	// every pixel of an image as ARGB (palette images included)
	static int[] Read( string path, out int width, out int height )
	{
		using ( var source = Image.FromFile( path ) )
		using ( var bitmap = new Bitmap( source.Width, source.Height, PixelFormat.Format32bppArgb ) )
		{
			using ( var graphics = Graphics.FromImage( bitmap ) )
			{
				graphics.DrawImage( source, new Rectangle( 0, 0, source.Width, source.Height ), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel );
			}

			width = bitmap.Width;
			height = bitmap.Height;

			var data = bitmap.LockBits( new Rectangle( 0, 0, width, height ), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb );
			var pixels = new int[ width * height ];

			for ( var y = 0; y < height; y++ )
			{
				Marshal.Copy( data.Scan0 + y * data.Stride, pixels, y * width, width );
			}

			bitmap.UnlockBits( data );

			return pixels;
		}
	}

	static void Write( string path, int[] argb, int width, int height, bool withAlpha )
	{
		using ( var bitmap = new Bitmap( width, height, PixelFormat.Format32bppArgb ) )
		{
			var data = bitmap.LockBits( new Rectangle( 0, 0, width, height ), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb );

			for ( var y = 0; y < height; y++ )
			{
				Marshal.Copy( argb, y * width, data.Scan0 + y * data.Stride, width );
			}

			bitmap.UnlockBits( data );

			if ( withAlpha )
			{
				bitmap.Save( path, ImageFormat.Png );
			}
			else
			{
				// the backgrounds of the project are RGB without alpha
				using ( var rgb = bitmap.Clone( new Rectangle( 0, 0, width, height ), PixelFormat.Format24bppRgb ) )
				{
					rgb.Save( path, ImageFormat.Png );
				}
			}
		}
	}

	// the magenta of the sensor window (the original's palette has 0xAA00AA; screenshots scaled with a filter come close to it)
	static bool IsMagenta( int argb )
	{
		int r = ( argb >> 16 ) & 255, g = ( argb >> 8 ) & 255, b = argb & 255;

		return ( r > 120 ) && ( b > 120 ) && ( g < 80 ) && ( Math.Abs( r - b ) < 60 );
	}

	static bool IsBlack( int argb )
	{
		int r = ( argb >> 16 ) & 255, g = ( argb >> 8 ) & 255, b = argb & 255;

		return ( r < 40 ) && ( g < 40 ) && ( b < 40 );
	}

	// the bounding box of the magenta sensor window in the top right quarter of a screenshot
	static Rectangle FindWindow( int[] pixels, int width, int height )
	{
		int x0 = width, y0 = height, x1 = -1, y1 = -1;

		for ( var y = 0; y < height / 2; y++ )
		{
			for ( var x = width / 2; x < width; x++ )
			{
				if ( IsMagenta( pixels[ y * width + x ] ) )
				{
					x0 = Math.Min( x0, x );
					y0 = Math.Min( y0, y );
					x1 = Math.Max( x1, x );
					y1 = Math.Max( y1, y );
				}
			}
		}

		if ( x1 < 0 )
		{
			throw new InvalidOperationException( "no magenta sensor window in the top right quarter of the screenshot" );
		}

		return new Rectangle( x0, y0, x1 - x0 + 1, y1 - y0 + 1 );
	}

	// make the background and the mask
	// - every pixel of the window that is not magenta is the ship: black on white in the background
	// - every pixel that is neither magenta nor black is a coloured part: white and opaque in the mask, where the scan's colour noise shows
	// - scaleX and scaleY are canvas pixels per pixel of the original's 320 x 200 screen (a larger screenshot is converted); the ship is centred
	//   and made smaller if it would be wider than 80% or taller than 88% of the canvas
	// - tolerance (in 320 x 200 pixels) is how far a simplified outline may stray from the traced pixels
	public static string Make( string screenshotPath, string backgroundPath, string maskPath, double scaleX, double scaleY, double tolerance, bool joinDiagonals )
	{
		int width, height;
		var pixels = Read( screenshotPath, out width, out height );
		var window = FindWindow( pixels, width, height );

		var ship = new bool[ window.Width * window.Height ];
		var colour = new bool[ window.Width * window.Height ];
		int bx0 = window.Width, by0 = window.Height, bx1 = -1, by1 = -1;

		for ( var y = 0; y < window.Height; y++ )
		{
			for ( var x = 0; x < window.Width; x++ )
			{
				var c = pixels[ ( window.Y + y ) * width + window.X + x ];

				if ( IsMagenta( c ) )
				{
					continue;
				}

				ship[ y * window.Width + x ] = true;
				colour[ y * window.Width + x ] = !IsBlack( c );

				bx0 = Math.Min( bx0, x );
				by0 = Math.Min( by0, y );
				bx1 = Math.Max( bx1, x );
				by1 = Math.Max( by1, y );
			}
		}

		if ( bx1 < 0 )
		{
			throw new InvalidOperationException( "the sensor window is empty" );
		}

		// screenshot pixels per pixel of the 320 x 200 screen
		double unitX = width / 320.0, unitY = height / 200.0;

		// canvas pixels per screenshot pixel
		double sx = scaleX / unitX, sy = scaleY / unitY;

		var fit = Math.Min( 1.0, Math.Min( 0.80 * c_canvasWidth / ( ( bx1 - bx0 + 1 ) * sx ), 0.88 * c_canvasHeight / ( ( by1 - by0 + 1 ) * sy ) ) );

		sx *= fit;
		sy *= fit;

		double centreU = ( bx0 + bx1 + 1 ) / 2.0, centreV = ( by0 + by1 + 1 ) / 2.0;

		int loops, vertices, maskLoops, maskVertices;

		var shipCover = Vectorize.Fill( ship, window.Width, window.Height, centreU, centreV, sx, sy, c_canvasWidth, c_canvasHeight, tolerance * unitX, joinDiagonals, out loops, out vertices );
		var colourCover = Vectorize.Fill( colour, window.Width, window.Height, centreU, centreV, sx, sy, c_canvasWidth, c_canvasHeight, tolerance * unitX, joinDiagonals, out maskLoops, out maskVertices );

		var background = new int[ c_canvasWidth * c_canvasHeight ];
		var mask = new int[ c_canvasWidth * c_canvasHeight ];

		for ( var i = 0; i < background.Length; i++ )
		{
			var s = Math.Min( 1.0, shipCover[ i ] );

			// the coloured parts are part of the ship
			var c = Math.Min( s, Math.Min( 1.0, colourCover[ i ] ) );

			var grey = (int) Math.Round( 255.0 * ( 1.0 - s ) );

			background[ i ] = unchecked( (int) 0xFF000000 ) | ( grey << 16 ) | ( grey << 8 ) | grey;
			mask[ i ] = ( (int) Math.Round( 255.0 * c ) << 24 ) | 0xFFFFFF;
		}

		Write( backgroundPath, background, c_canvasWidth, c_canvasHeight, false );
		Write( maskPath, mask, c_canvasWidth, c_canvasHeight, true );

		return string.Format( "window={0}x{1} ship={2}x{3} fit={4:F3} scale={5:F2}x{6:F2} outlines={7} ({8} points) colouredParts={9} ({10} points)", window.Width, window.Height, bx1 - bx0 + 1, by1 - by0 + 1, fit, sx * unitX, sy * unitY, loops, vertices, maskLoops, maskVertices );
	}

	// a review sheet: the original's sensor window on the left, and on the right roughly what the port's window shows at the end of a scan
	// (magenta where the background is white, black where it is black, colour noise where the mask is opaque)
	public static void Sheet( string screenshotPath, string backgroundPath, string maskPath, string sheetPath )
	{
		int width, height;
		var pixels = Read( screenshotPath, out width, out height );
		var window = FindWindow( pixels, width, height );

		int bw, bh, mw, mh;
		var background = Read( backgroundPath, out bw, out bh );
		var mask = Read( maskPath, out mw, out mh );

		var random = new Random( 1 );
		var picture = new int[ bw * bh ];

		for ( var i = 0; i < picture.Length; i++ )
		{
			var light = ( background[ i ] & 255 ) / 255.0;
			var alpha = ( ( mask[ i ] >> 24 ) & 255 ) / 255.0;

			double r = 170 * light, g = 0, b = 170 * light;

			if ( alpha > 0 )
			{
				r = r * ( 1 - alpha ) + random.Next( 256 ) * alpha;
				g = g * ( 1 - alpha ) + random.Next( 256 ) * alpha;
				b = b * ( 1 - alpha ) + random.Next( 256 ) * alpha;
			}

			picture[ i ] = unchecked( (int) 0xFF000000 ) | ( (int) r << 16 ) | ( (int) g << 8 ) | (int) b;
		}

		// both halves 512 pixels high: the original window keeps the shape it has on a 4:3 screen, the port's window is 2:1
		const int halfHeight = 512;
		var originalWidth = (int) Math.Round( halfHeight * ( window.Width / ( width / 320.0 ) ) / ( window.Height / ( height / 200.0 ) * 1.2 ) );

		using ( var sheet = new Bitmap( originalWidth + 16 + 1024, halfHeight, PixelFormat.Format32bppArgb ) )
		using ( var graphics = Graphics.FromImage( sheet ) )
		using ( var source = Image.FromFile( screenshotPath ) )
		using ( var port = new Bitmap( bw, bh, PixelFormat.Format32bppArgb ) )
		{
			var data = port.LockBits( new Rectangle( 0, 0, bw, bh ), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb );

			for ( var y = 0; y < bh; y++ )
			{
				Marshal.Copy( picture, y * bw, data.Scan0 + y * data.Stride, bw );
			}

			port.UnlockBits( data );

			graphics.Clear( Color.White );
			graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
			graphics.PixelOffsetMode = PixelOffsetMode.Half;
			graphics.DrawImage( source, new Rectangle( 0, 0, originalWidth, halfHeight ), window, GraphicsUnit.Pixel );
			graphics.DrawImage( port, new Rectangle( originalWidth + 16, 0, 1024, halfHeight ) );

			sheet.Save( sheetPath, ImageFormat.Png );
		}
	}
}

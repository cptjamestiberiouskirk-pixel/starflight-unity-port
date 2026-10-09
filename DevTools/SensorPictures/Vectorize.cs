using System;
using System.Collections.Generic;

// turns a small binary image (pixel art) into polygons and fills them at a larger size
public static class Vectorize
{
	struct Point
	{
		public double X;
		public double Y;

		public Point( double x, double y )
		{
			X = x;
			Y = y;
		}
	}

	static long Key( int x, int y )
	{
		return ( (long) y << 32 ) | (uint) x;
	}

	static int KeyX( long key )
	{
		return (int) ( key & 0xFFFFFFFF );
	}

	static int KeyY( long key )
	{
		return (int) ( key >> 32 );
	}

	// the outlines of the set pixels along the pixel edges, one closed loop per boundary (outer edges and holes alike), as the midpoints of the edges
	// (a midpoint outline turns a regular staircase into a straight line)
	static List<List<Point>> Outlines( bool[] set, int width, int height, bool joinDiagonals )
	{
		Func<int, int, bool> isSet = ( x, y ) => ( x >= 0 ) && ( y >= 0 ) && ( x < width ) && ( y < height ) && set[ y * width + x ];

		// directed edges from corner to corner, every edge with its set pixel on the same side
		var edges = new Dictionary<long, List<long>>();

		Action<int, int, int, int> addEdge = ( x0, y0, x1, y1 ) =>
		{
			List<long> list;
			var key = Key( x0, y0 );

			if ( !edges.TryGetValue( key, out list ) )
			{
				list = new List<long>();
				edges[ key ] = list;
			}

			list.Add( Key( x1, y1 ) );
		};

		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				if ( !isSet( x, y ) )
				{
					continue;
				}

				if ( !isSet( x, y - 1 ) ) addEdge( x + 1, y, x, y );
				if ( !isSet( x - 1, y ) ) addEdge( x, y, x, y + 1 );
				if ( !isSet( x, y + 1 ) ) addEdge( x, y + 1, x + 1, y + 1 );
				if ( !isSet( x + 1, y ) ) addEdge( x + 1, y + 1, x + 1, y );
			}
		}

		var loops = new List<List<Point>>();

		for ( var loopGuard = 0; ( edges.Count > 0 ) && ( loopGuard < 100000 ); loopGuard++ )
		{
			long start = 0;

			foreach ( var key in edges.Keys )
			{
				start = key;
				break;
			}

			var corners = new List<long>();
			long current = start;
			long previous = -1;

			for ( var step = 0; step < 1000000; step++ )
			{
				List<long> outgoing;

				if ( !edges.TryGetValue( current, out outgoing ) )
				{
					break;
				}

				var next = outgoing[ 0 ];

				// two pixels that touch at a corner only: turn so as to join them (or to keep them apart)
				if ( ( outgoing.Count > 1 ) && ( previous >= 0 ) )
				{
					int dx = KeyX( current ) - KeyX( previous ), dy = KeyY( current ) - KeyY( previous );
					var best = double.MinValue;

					foreach ( var candidate in outgoing )
					{
						int ox = KeyX( candidate ) - KeyX( current ), oy = KeyY( candidate ) - KeyY( current );
						double turn = dx * oy - dy * ox;

						if ( !joinDiagonals )
						{
							turn = -turn;
						}

						if ( turn > best )
						{
							best = turn;
							next = candidate;
						}
					}
				}

				outgoing.Remove( next );

				if ( outgoing.Count == 0 )
				{
					edges.Remove( current );
				}

				corners.Add( current );
				previous = current;
				current = next;

				if ( current == start )
				{
					break;
				}
			}

			if ( corners.Count < 3 )
			{
				continue;
			}

			var loop = new List<Point>();

			for ( var i = 0; i < corners.Count; i++ )
			{
				var a = corners[ i ];
				var b = corners[ ( i + 1 ) % corners.Count ];

				loop.Add( new Point( ( KeyX( a ) + KeyX( b ) ) / 2.0, ( KeyY( a ) + KeyY( b ) ) / 2.0 ) );
			}

			loops.Add( loop );
		}

		return loops;
	}

	// douglas-peucker on a closed loop: drop the points that are less than the tolerance away from the outline without them
	static List<Point> Simplify( List<Point> loop, double tolerance )
	{
		if ( loop.Count < 4 )
		{
			return loop;
		}

		// split the loop at the point farthest from the first one
		var far = 0;
		var farDistance = -1.0;

		for ( var i = 1; i < loop.Count; i++ )
		{
			var distance = DistanceSquared( loop[ 0 ], loop[ i ] );

			if ( distance > farDistance )
			{
				farDistance = distance;
				far = i;
			}
		}

		var keep = new bool[ loop.Count ];
		keep[ 0 ] = true;
		keep[ far ] = true;

		Mark( loop, 0, far, tolerance, keep );
		Mark( loop, far, loop.Count, tolerance, keep );

		var result = new List<Point>();

		for ( var i = 0; i < loop.Count; i++ )
		{
			if ( keep[ i ] )
			{
				result.Add( loop[ i ] );
			}
		}

		return result;
	}

	static void Mark( List<Point> loop, int a, int b, double tolerance, bool[] keep )
	{
		if ( b - a < 2 )
		{
			return;
		}

		var pointA = loop[ a ];
		var pointB = loop[ b % loop.Count ];
		var index = -1;
		var max = -1.0;

		for ( var i = a + 1; i < b; i++ )
		{
			var distance = SegmentDistance( loop[ i ], pointA, pointB );

			if ( distance > max )
			{
				max = distance;
				index = i;
			}
		}

		if ( max > tolerance )
		{
			keep[ index ] = true;

			Mark( loop, a, index, tolerance, keep );
			Mark( loop, index, b, tolerance, keep );
		}
	}

	static double DistanceSquared( Point a, Point b )
	{
		return ( a.X - b.X ) * ( a.X - b.X ) + ( a.Y - b.Y ) * ( a.Y - b.Y );
	}

	static double SegmentDistance( Point p, Point a, Point b )
	{
		double dx = b.X - a.X, dy = b.Y - a.Y, lengthSquared = dx * dx + dy * dy;

		if ( lengthSquared == 0 )
		{
			return Math.Sqrt( DistanceSquared( p, a ) );
		}

		var t = Math.Max( 0, Math.Min( 1, ( ( p.X - a.X ) * dx + ( p.Y - a.Y ) * dy ) / lengthSquared ) );

		return Math.Sqrt( DistanceSquared( p, new Point( a.X + t * dx, a.Y + t * dy ) ) );
	}

	// how much of each canvas pixel (0 to 1) the set pixels cover once traced, simplified and drawn with source point (u, v) at
	// canvas ( ( u - centreU ) * scaleX + canvasWidth / 2, ( v - centreV ) * scaleY + canvasHeight / 2 ); even-odd fill, 4 x 4 samples per pixel
	public static double[] Fill( bool[] set, int width, int height, double centreU, double centreV, double scaleX, double scaleY, int canvasWidth, int canvasHeight, double tolerance, bool joinDiagonals, out int loopCount, out int vertexCount )
	{
		var x0List = new List<double>();
		var y0List = new List<double>();
		var x1List = new List<double>();
		var y1List = new List<double>();

		loopCount = 0;
		vertexCount = 0;

		foreach ( var traced in Outlines( set, width, height, joinDiagonals ) )
		{
			var loop = Simplify( traced, tolerance );

			if ( loop.Count < 3 )
			{
				continue;
			}

			loopCount++;
			vertexCount += loop.Count;

			for ( var i = 0; i < loop.Count; i++ )
			{
				var a = loop[ i ];
				var b = loop[ ( i + 1 ) % loop.Count ];

				x0List.Add( ( a.X - centreU ) * scaleX + canvasWidth / 2.0 );
				y0List.Add( ( a.Y - centreV ) * scaleY + canvasHeight / 2.0 );
				x1List.Add( ( b.X - centreU ) * scaleX + canvasWidth / 2.0 );
				y1List.Add( ( b.Y - centreV ) * scaleY + canvasHeight / 2.0 );
			}
		}

		const int samples = 4;

		var coverage = new double[ canvasWidth * canvasHeight ];
		var crossings = new List<double>();

		for ( var row = 0; row < canvasHeight * samples; row++ )
		{
			var y = ( row + 0.5 ) / samples;

			crossings.Clear();

			for ( var e = 0; e < x0List.Count; e++ )
			{
				double y0 = y0List[ e ], y1 = y1List[ e ];

				if ( ( ( y0 <= y ) && ( y1 > y ) ) || ( ( y1 <= y ) && ( y0 > y ) ) )
				{
					crossings.Add( x0List[ e ] + ( y - y0 ) / ( y1 - y0 ) * ( x1List[ e ] - x0List[ e ] ) );
				}
			}

			crossings.Sort();

			for ( var k = 0; k + 1 < crossings.Count; k += 2 )
			{
				// the samples whose centre lies inside the span
				var first = (int) Math.Ceiling( crossings[ k ] * samples - 0.5 );
				var last = (int) Math.Floor( crossings[ k + 1 ] * samples - 0.5 );

				for ( var s = Math.Max( 0, first ); s <= Math.Min( canvasWidth * samples - 1, last ); s++ )
				{
					coverage[ ( row / samples ) * canvasWidth + s / samples ] += 1.0 / ( samples * samples );
				}
			}
		}

		return coverage;
	}
}

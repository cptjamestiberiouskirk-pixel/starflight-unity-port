<?php

// The admin login of the planet pages. The password is checked against a hash kept in
// admin-config.php (not in git, see admin-config.example.php), a logged in session is marked
// as the admin's, and every admin form carries the session's token, which every admin action
// checks before it changes anything. Failed logins are counted, per address and in all, and
// past a few the login closes for a while that doubles with each further failure, so that a
// password cannot be guessed however many sessions the guesses come from.

const ADMIN_CONFIG_FILE = __DIR__ . '/admin-config.php';

// the failed logins from one address, and from all addresses together, after which the login
// closes; how long it closes for then (doubling with each further failure) and at most; and how
// long after its last failure a count is forgotten
const ADMIN_FREE_FAILURES_PER_ADDRESS = 5;
const ADMIN_FREE_FAILURES_IN_ALL = 20;
const ADMIN_FIRST_LOCK_SECONDS = 30;
const ADMIN_LONGEST_LOCK_SECONDS = 3600;
const ADMIN_FORGET_SECONDS = 86400;

// the most addresses whose failures are kept (the oldest go first), so that the file stays small
const ADMIN_MOST_ADDRESSES = 10000;

// starts the session; a visitor without a session cookie gets one only when $always is true
// (the admin login), so the public pages set no cookie
function AdminStartSession( bool $always )
{
	if ( session_status() === PHP_SESSION_ACTIVE )
	{
		return;
	}
	
	session_name( 'StarflightPlanetsAdmin' );
	
	if ( !$always && !isset( $_COOKIE[ session_name() ] ) )
	{
		return;
	}
	
	// never take a session id this server did not make
	ini_set( 'session.use_strict_mode', '1' );
	
	$https = !empty( $_SERVER[ 'HTTPS' ] ) && ( $_SERVER[ 'HTTPS' ] !== 'off' );
	
	$path = rtrim( dirname( $_SERVER[ 'SCRIPT_NAME' ] ), '/\\' ) . '/';
	
	// Lax keeps the cookie off every POST another site makes, and the token stops the rest
	session_set_cookie_params( [ 'lifetime' => 0, 'path' => $path, 'secure' => $https, 'httponly' => true, 'samesite' => 'Lax' ] );
	
	session_start();
}

// the hash of the admin password, or null when the login is not set up (then nobody can log in)
function AdminPasswordHash() : ?string
{
	if ( !is_file( ADMIN_CONFIG_FILE ) )
	{
		return null;
	}
	
	$config = include ADMIN_CONFIG_FILE;
	
	$hash = is_array( $config ) ? ( $config[ 'passwordHash' ] ?? '' ) : '';
	
	return ( is_string( $hash ) && ( $hash !== '' ) ) ? $hash : null;
}

function AdminIsLoggedIn() : bool
{
	return ( session_status() === PHP_SESSION_ACTIVE ) && ( ( $_SESSION[ 'admin' ] ?? false ) === true );
}

// the token the admin forms carry; starts a session if there is none, so call it before the page is written
function AdminToken() : string
{
	AdminStartSession( true );
	
	if ( empty( $_SESSION[ 'token' ] ) || !is_string( $_SESSION[ 'token' ] ) )
	{
		$_SESSION[ 'token' ] = bin2hex( random_bytes( 32 ) );
	}
	
	return $_SESSION[ 'token' ];
}

// whether the POST carries this session's token
function AdminTokenIsValid() : bool
{
	if ( session_status() !== PHP_SESSION_ACTIVE )
	{
		return false;
	}
	
	$token = filter_input( INPUT_POST, 'token' );
	
	$expected = $_SESSION[ 'token' ] ?? '';
	
	return is_string( $token ) && is_string( $expected ) && ( $expected !== '' ) && hash_equals( $expected, $token );
}

// the file the failed logins are counted in: outside the pages, in the system's temporary folder,
// unless admin-config.php names another ('throttleFile')
function AdminThrottleFile() : string
{
	if ( is_file( ADMIN_CONFIG_FILE ) )
	{
		$config = include ADMIN_CONFIG_FILE;
		
		$file = is_array( $config ) ? ( $config[ 'throttleFile' ] ?? '' ) : '';
		
		if ( is_string( $file ) && ( $file !== '' ) )
		{
			return $file;
		}
	}
	
	return rtrim( sys_get_temp_dir(), '/\\' ) . '/starflight-planets-admin-logins.json';
}

// the address a login comes from, as a hash (the counts keep no address): the connection's own,
// never a forwarded-for header, which any visitor can set
function AdminAddressKey() : string
{
	return hash( 'sha256', (string) ( $_SERVER[ 'REMOTE_ADDR' ] ?? '' ) );
}

// reads the counts, lets $change alter them and writes them back, all under a lock on the file,
// and returns what $change returns; without the file nothing is counted (the login then works as
// before, slowed by a second a failure) and the server's error log says so
function AdminThrottle( callable $change )
{
	$file = AdminThrottleFile();
	$handle = @fopen( $file, 'c+' );
	
	if ( $handle === false )
	{
		error_log( 'Starflight planets: the failed logins cannot be counted in ' . $file );
		
		$state = [];
		
		return $change( $state );
	}
	
	flock( $handle, LOCK_EX );
	
	$text = stream_get_contents( $handle );
	$state = json_decode( ( $text === false ) || ( $text === '' ) ? '[]' : $text, true );
	
	if ( !is_array( $state ) )
	{
		$state = [];
	}
	
	$result = $change( $state );
	
	ftruncate( $handle, 0 );
	rewind( $handle );
	fwrite( $handle, json_encode( $state ) );
	fflush( $handle );
	flock( $handle, LOCK_UN );
	fclose( $handle );
	
	return $result;
}

// how long the login closes after this many failures: none while they are fewer than the free ones,
// then the first lock, doubling with each further failure, at most the longest
function AdminLockSeconds( int $failures, int $free ) : int
{
	if ( $failures < $free )
	{
		return 0;
	}
	
	return (int) min( ADMIN_LONGEST_LOCK_SECONDS, ADMIN_FIRST_LOCK_SECONDS * ( 2 ** min( 20, $failures - $free ) ) );
}

// forgets the counts whose last failure is older than ADMIN_FORGET_SECONDS, and the oldest
// addresses past ADMIN_MOST_ADDRESSES
function AdminForgetOldFailures( array &$state, int $now )
{
	$addresses = is_array( $state[ 'addresses' ] ?? null ) ? $state[ 'addresses' ] : [];
	
	foreach ( $addresses as $key => $record )
	{
		if ( !is_array( $record ) || ( (int) ( $record[ 'last' ] ?? 0 ) < $now - ADMIN_FORGET_SECONDS ) )
		{
			unset( $addresses[ $key ] );
		}
	}
	
	if ( count( $addresses ) > ADMIN_MOST_ADDRESSES )
	{
		uasort( $addresses, function ( $a, $b ) { return (int) $a[ 'last' ] <=> (int) $b[ 'last' ]; } );
		
		$addresses = array_slice( $addresses, count( $addresses ) - ADMIN_MOST_ADDRESSES, null, true );
	}
	
	$state[ 'addresses' ] = $addresses;
	
	if ( !is_array( $state[ 'all' ] ?? null ) || ( (int) ( $state[ 'all' ][ 'last' ] ?? 0 ) < $now - ADMIN_FORGET_SECONDS ) )
	{
		$state[ 'all' ] = [ 'failures' => 0, 'last' => 0, 'until' => 0 ];
	}
}

// how many seconds the login stays closed for this address (0 while it is open)
function AdminLoginLockSeconds() : int
{
	return AdminThrottle( function ( array &$state )
	{
		$now = time();
		
		AdminForgetOldFailures( $state, $now );
		
		$until = max( (int) ( $state[ 'addresses' ][ AdminAddressKey() ][ 'until' ] ?? 0 ), (int) $state[ 'all' ][ 'until' ] );
		
		return max( 0, $until - $now );
	} );
}

// counts a failed login, for this address and in all
function AdminCountFailedLogin()
{
	AdminThrottle( function ( array &$state )
	{
		$now = time();
		
		AdminForgetOldFailures( $state, $now );
		
		$key = AdminAddressKey();
		$address = $state[ 'addresses' ][ $key ] ?? [ 'failures' => 0, 'last' => 0, 'until' => 0 ];
		
		$address[ 'failures' ] = (int) $address[ 'failures' ] + 1;
		$address[ 'last' ] = $now;
		$address[ 'until' ] = $now + AdminLockSeconds( $address[ 'failures' ], ADMIN_FREE_FAILURES_PER_ADDRESS );
		
		$state[ 'addresses' ][ $key ] = $address;
		
		$all = $state[ 'all' ];
		
		$all[ 'failures' ] = (int) $all[ 'failures' ] + 1;
		$all[ 'last' ] = $now;
		$all[ 'until' ] = $now + AdminLockSeconds( $all[ 'failures' ], ADMIN_FREE_FAILURES_IN_ALL );
		
		$state[ 'all' ] = $all;
	} );
}

// forgets this address's failures (it has logged in)
function AdminForgetAddressFailures()
{
	AdminThrottle( function ( array &$state )
	{
		unset( $state[ 'addresses' ][ AdminAddressKey() ] );
	} );
}

// returns false for a wrong password, and without checking it while the login is closed; a failed
// login also gives the form a new token, so a guess cannot be sent again with the old one
function AdminLogIn( string $password ) : bool
{
	if ( AdminLoginLockSeconds() > 0 )
	{
		$_SESSION[ 'token' ] = bin2hex( random_bytes( 32 ) );
		
		return false;
	}
	
	$hash = AdminPasswordHash();
	
	if ( ( $hash === null ) || !password_verify( $password, $hash ) )
	{
		AdminCountFailedLogin();
		
		$_SESSION[ 'token' ] = bin2hex( random_bytes( 32 ) );
		
		// slow down guessing
		sleep( 1 );
		
		return false;
	}
	
	AdminForgetAddressFailures();
	
	// a new session id and a new token, so nothing from before the login carries over
	session_regenerate_id( true );
	
	$_SESSION[ 'admin' ] = true;
	$_SESSION[ 'token' ] = bin2hex( random_bytes( 32 ) );
	
	return true;
}

function AdminLogOut()
{
	$_SESSION = [];
	
	session_regenerate_id( true );
}

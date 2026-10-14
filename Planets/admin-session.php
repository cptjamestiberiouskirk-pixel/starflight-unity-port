<?php

// The admin login of the planet pages. The password is checked against a hash kept in
// admin-config.php (not in git, see admin-config.example.php), a logged in session is marked
// as the admin's, and every admin form carries the session's token, which every admin action
// checks before it changes anything.

const ADMIN_CONFIG_FILE = __DIR__ . '/admin-config.php';

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

function AdminLogIn( string $password ) : bool
{
	$hash = AdminPasswordHash();
	
	if ( ( $hash === null ) || !password_verify( $password, $hash ) )
	{
		// slow down guessing
		sleep( 1 );
		
		return false;
	}
	
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

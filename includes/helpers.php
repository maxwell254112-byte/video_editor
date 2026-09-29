<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli' && isset($_SERVER['SCRIPT_FILENAME']) && realpath((string) $_SERVER['SCRIPT_FILENAME']) === realpath(__FILE__)) {
    http_response_code(404);
    exit;
}

final class AppException extends RuntimeException
{
}

function boot_api(bool $json = true): void
{
    $config = app_config();
    ini_set('display_errors', '0');
    ini_set('log_errors', '1');
    error_reporting(E_ALL);
    ensure_app_directories();
    ini_set('error_log', $config['log_directory'] . DIRECTORY_SEPARATOR . 'php-error.log');

    if (PHP_SAPI === 'cli' || !$json) {
        return;
    }

    header('Content-Type: application/json; charset=utf-8');
    header('X-Content-Type-Options: nosniff');
    header('Cache-Control: no-store');

    set_exception_handler(static function (Throwable $e): void {
        if ($e instanceof AppException) {
            $status = $e->getCode() >= 400 && $e->getCode() < 600 ? $e->getCode() : 400;
            json_response(['ok' => false, 'message' => $e->getMessage()], $status);
        }
        log_error($e->getMessage() . ' @ ' . $e->getFile() . ':' . $e->getLine());
        json_response(['ok' => false, 'message' => 'Unexpected server error.'], 500);
    });
}

function json_response(array $data, int $status = 200): never
{
    http_response_code($status);
    if (!headers_sent()) {
        header('Content-Type: application/json; charset=utf-8');
        header('X-Content-Type-Options: nosniff');
        header('Cache-Control: no-store');
    }
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
    exit;
}

function read_json_body(): array
{
    $raw = file_get_contents('php://input');
    if ($raw === false || trim($raw) === '') {
        throw new AppException('Invalid project data.');
    }
    if (strlen($raw) > (int) app_config()['max_project_bytes']) {
        throw new AppException('Invalid project data.');
    }
    try {
        $data = json_decode($raw, true, 32, JSON_THROW_ON_ERROR);
    } catch (JsonException) {
        throw new AppException('Invalid project data.');
    }
    if (!is_array($data)) {
        throw new AppException('Invalid project data.');
    }
    return $data;
}

function assert_post(): void
{
    if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
        throw new AppException('Invalid request.', 405);
    }
}

function assert_same_origin(): void
{
    $origin = (string) ($_SERVER['HTTP_ORIGIN'] ?? '');
    if ($origin === '') {
        return;
    }
    $host = (string) ($_SERVER['HTTP_HOST'] ?? '');
    $originHost = (string) parse_url($origin, PHP_URL_HOST);
    $serverHost = explode(':', $host)[0];
    if ($originHost === '' || strcasecmp($originHost, $serverHost) !== 0) {
        throw new AppException('Invalid request.', 403);
    }
}

function log_error(string $message): void
{
    try {
        ensure_app_directories();
        $dir = app_config()['log_directory'];
        $file = $dir . DIRECTORY_SEPARATOR . 'app.log';
        if (is_file($file) && filesize($file) > 2_000_000) {
            @rename($file, $dir . DIRECTORY_SEPARATOR . 'app.log.old');
        }
        $line = gmdate('c') . ' ' . str_replace(["\r", "\n"], ' ', $message) . PHP_EOL;
        file_put_contents($file, $line, FILE_APPEND | LOCK_EX);
    } catch (Throwable) {
        // Logging must never break the request.
    }
}

function ensure_app_directories(): void
{
    $config = app_config();
    $dirs = [
        $config['upload_directory'] . '/videos',
        $config['upload_directory'] . '/images',
        $config['upload_directory'] . '/audio',
        $config['upload_directory'] . '/meta',
        $config['upload_directory'] . '/thumbs',
        $config['upload_directory'] . '/previews',
        $config['export_directory'] . '/jobs',
        $config['log_directory'],
    ];
    foreach ($dirs as $dir) {
        if (!is_dir($dir) && !mkdir($dir, 0755, true) && !is_dir($dir)) {
            throw new AppException('Server storage is not available.', 500);
        }
    }
}

function app_writable(): bool
{
    $config = app_config();
    return is_writable($config['upload_directory']) && is_writable($config['export_directory']);
}

function new_id(): string
{
    return bin2hex(random_bytes(16));
}

function is_token(string $value, int $length = 32): bool
{
    return (bool) preg_match('/^[a-f0-9]{' . $length . '}$/', $value);
}

function safe_filename(string $original, string $fallbackExt): string
{
    $base = basename(str_replace('\\', '/', $original));
    $base = preg_replace('/[^\p{L}\p{N}\s._()-]/u', '', $base) ?? '';
    $base = trim((string) preg_replace('/\s+/', ' ', $base));
    if ($base === '' || $base === '.' || $base === '..') {
        $base = 'media.' . $fallbackExt;
    }
    if (function_exists('mb_substr')) {
        return mb_substr($base, 0, 120);
    }
    return substr($base, 0, 120);
}

function ff(float $number): string
{
    $formatted = sprintf('%.4F', $number);
    if (str_contains($formatted, '.')) {
        $formatted = rtrim(rtrim($formatted, '0'), '.');
    }
    if ($formatted === '' || $formatted === '-' || $formatted === '-0') {
        return '0';
    }
    return $formatted;
}

function clamp_float(mixed $value, float $min, float $max, float $default): float
{
    if (!is_numeric($value)) {
        return $default;
    }
    $number = (float) $value;
    if (!is_finite($number)) {
        return $default;
    }
    return max($min, min($max, $number));
}

function clamp_int(mixed $value, int $min, int $max, int $default): int
{
    if (!is_numeric($value)) {
        return $default;
    }
    return max($min, min($max, (int) round((float) $value)));
}

function normalize_bool(mixed $value): bool
{
    return $value === true || $value === 1 || $value === '1' || $value === 'true';
}

function process_functions_available(): bool
{
    if (!function_exists('proc_open')) {
        return false;
    }
    $disabled = array_map('trim', explode(',', (string) ini_get('disable_functions')));
    return !in_array('proc_open', $disabled, true);
}

/**
 * @param array<int, string> $command
 * @return array{code:int, stdout:string, stderr:string}
 */
function run_process(array $command, int $timeoutSeconds = 20): array
{
    if (!process_functions_available()) {
        return ['code' => 1, 'stdout' => '', 'stderr' => 'proc_open is disabled'];
    }

    $descriptors = [
        0 => ['pipe', 'r'],
        1 => ['pipe', 'w'],
        2 => ['pipe', 'w'],
    ];
    $proc = proc_open($command, $descriptors, $pipes, null, null, ['bypass_shell' => true]);
    if (!is_resource($proc)) {
        return ['code' => 1, 'stdout' => '', 'stderr' => 'Could not start process'];
    }
    fclose($pipes[0]);
    stream_set_blocking($pipes[1], false);
    stream_set_blocking($pipes[2], false);
    $stdout = '';
    $stderr = '';
    $start = time();
    $exitCode = 1;
    while (true) {
        $stdout .= (string) stream_get_contents($pipes[1]);
        $stderr .= (string) stream_get_contents($pipes[2]);
        $status = proc_get_status($proc);
        if (!$status['running']) {
            $stdout .= (string) stream_get_contents($pipes[1]);
            $stderr .= (string) stream_get_contents($pipes[2]);
            $exitCode = (int) $status['exitcode'];
            break;
        }
        if ($timeoutSeconds > 0 && (time() - $start) > $timeoutSeconds) {
            proc_terminate($proc);
            $stderr .= ' Process timed out.';
            break;
        }
        usleep(100000);
    }
    fclose($pipes[1]);
    fclose($pipes[2]);
    $closeCode = proc_close($proc);
    if ($exitCode < 0) {
        $exitCode = $closeCode;
    }
    return ['code' => $exitCode, 'stdout' => $stdout, 'stderr' => $stderr];
}

/**
 * @param array<int, string> $command
 */
function run_process_logged(array $command, string $logFile): int
{
    if (!process_functions_available()) {
        throw new AppException('FFmpeg is unavailable.');
    }
    $proc = proc_open($command, [
        0 => ['pipe', 'r'],
        1 => ['file', $logFile, 'a'],
        2 => ['file', $logFile, 'a'],
    ], $pipes, null, null, ['bypass_shell' => true]);
    if (!is_resource($proc)) {
        throw new AppException('FFmpeg is unavailable.');
    }
    fclose($pipes[0]);
    return proc_close($proc);
}

function php_cli_binary(): ?string
{
    $configured = (string) app_config()['php_cli'];
    $candidates = array_filter([
        $configured,
        'C:/xampp/php/php.exe',
        'C:/php/php.exe',
    ]);
    foreach ($candidates as $path) {
        if (is_file($path)) {
            return $path;
        }
    }
    $binary = PHP_BINARY;
    $base = strtolower(basename($binary));
    if ($base === 'php' && is_file($binary)) {
        return $binary;
    }
    $sibling = dirname($binary) . DIRECTORY_SEPARATOR . 'php';
    if (is_file($sibling)) {
        return $sibling;
    }
    if (defined('PHP_BINDIR')) {
        $bindir = PHP_BINDIR . DIRECTORY_SEPARATOR . 'php';
        if (is_file($bindir)) {
            return $bindir;
        }
    }
    foreach (['83', '82', '81', '84', '80', '74'] as $version) {
        foreach ([
            '/opt/cpanel/ea-php' . $version . '/root/usr/bin/php',
            '/usr/local/bin/ea-php' . $version,
            '/opt/alt/php' . $version . '/usr/bin/php',
            '/usr/bin/php' . $version,
        ] as $path) {
            if (is_file($path)) {
                return $path;
            }
        }
    }
    foreach (['/usr/local/bin/php', '/usr/bin/php'] as $path) {
        if (is_file($path)) {
            return $path;
        }
    }
    if (process_functions_available()) {
        $finder = PHP_OS_FAMILY === 'Windows' ? ['where.exe', 'php'] : ['which', 'php'];
        $found = run_process($finder, 5);
        if ($found['code'] === 0) {
            foreach (preg_split('/\R/', $found['stdout']) ?: [] as $line) {
                $line = trim($line);
                if ($line !== '' && is_file($line) && str_starts_with(strtolower(basename($line)), 'php')) {
                    return $line;
                }
            }
        }
    }
    return null;
}

/**
 * @param array<int, string> $command
 */
function spawn_detached(array $command): void
{
    if (!process_functions_available()) {
        throw new AppException('PHP command execution is disabled. Enable proc_open to export video.');
    }
    if (PHP_OS_FAMILY === 'Windows') {
        $parts = [];
        foreach ($command as $part) {
            $parts[] = '"' . str_replace('"', '', $part) . '"';
        }
        $line = 'start /B "" ' . implode(' ', $parts);
        $proc = proc_open($line, [
            0 => ['pipe', 'r'],
            1 => ['pipe', 'w'],
            2 => ['pipe', 'w'],
        ], $pipes);
        if (!is_resource($proc)) {
            throw new AppException('Could not start the export worker.');
        }
        foreach ($pipes as $pipe) {
            fclose($pipe);
        }
        proc_close($proc);
        return;
    }

    $escaped = array_map(static fn (string $part): string => escapeshellarg($part), $command);
    $line = implode(' ', $escaped) . ' > /dev/null 2>&1 &';
    exec($line);
}

function storage_file(string $root, string $subdir, string $filename): string
{
    if (!preg_match('/^[a-z0-9_-]+$/i', $subdir)) {
        throw new AppException('Invalid media path.');
    }
    if (!preg_match('/^[a-f0-9]{32}\.[a-z0-9]{2,5}$/i', $filename) && !preg_match('/^[a-f0-9]{32}$/', $filename)) {
        throw new AppException('Invalid media path.');
    }
    $rootReal = realpath($root);
    $dirReal = realpath($root . DIRECTORY_SEPARATOR . $subdir);
    if ($rootReal === false || $dirReal === false) {
        throw new AppException('Invalid media path.');
    }
    $rootNorm = rtrim(str_replace('\\', '/', $rootReal), '/') . '/';
    $dirNorm = str_replace('\\', '/', $dirReal);
    if (!str_starts_with($dirNorm . '/', $rootNorm) && $dirNorm !== rtrim($rootNorm, '/')) {
        throw new AppException('Invalid media path.');
    }
    return $dirReal . DIRECTORY_SEPARATOR . $filename;
}

function sanitize_public_error(string $text): string
{
    $text = preg_replace('/[A-Za-z]:\\\\[^\s]+/', '[path]', $text) ?? $text;
    $text = preg_replace('#/(?:home|var|usr|tmp)/[^\s]+#', '[path]', $text) ?? $text;
    $text = trim(preg_replace('/\s+/', ' ', $text) ?? $text);
    if ($text === '') {
        return 'Export failed.';
    }
    if (strlen($text) > 240) {
        $text = substr($text, 0, 240) . '…';
    }
    return $text;
}

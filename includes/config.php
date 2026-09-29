<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli' && isset($_SERVER['SCRIPT_FILENAME']) && realpath((string) $_SERVER['SCRIPT_FILENAME']) === realpath(__FILE__)) {
    http_response_code(404);
    exit;
}

/**
 * Central configuration. Override any key in includes/config.local.php.
 */
function app_config(): array
{
    static $config = null;
    if ($config !== null) {
        return $config;
    }

    $root = dirname(__DIR__);
    $config = [
        'app_name' => 'Video Editor',
        'root' => $root,
        'max_video_size' => 500 * 1024 * 1024,
        'max_image_size' => 20 * 1024 * 1024,
        'max_audio_size' => 50 * 1024 * 1024,
        'ffmpeg_path' => '',
        'ffprobe_path' => '',
        'php_cli' => '',
        'upload_directory' => $root . DIRECTORY_SEPARATOR . 'uploads',
        'export_directory' => $root . DIRECTORY_SEPARATOR . 'exports',
        'log_directory' => $root . DIRECTORY_SEPARATOR . 'storage' . DIRECTORY_SEPARATOR . 'logs',
        'composition_width' => 1920,
        'composition_height' => 1080,
        'composition_fps' => 30,
        'max_project_bytes' => 2_000_000,
        'history_limit' => 50,
    ];

    $local = __DIR__ . DIRECTORY_SEPARATOR . 'config.local.php';
    if (is_file($local)) {
        $override = require $local;
        if (is_array($override)) {
            $config = array_merge($config, $override);
        }
    }

    return $config;
}

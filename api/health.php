<?php
declare(strict_types=1);

require_once dirname(__DIR__) . '/includes/bootstrap.php';
boot_api();

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'GET') {
    throw new AppException('Invalid request.', 405);
}

$status = ffmpeg_status();
$config = app_config();
json_response([
    'ok' => true,
    'message' => $status['message'],
    'ffmpeg' => $status['ffmpeg'] && $status['ffprobe'],
    'ffprobe' => $status['ffprobe'],
    'phpCli' => $status['phpCli'],
    'writable' => $status['writable'],
    'version' => $status['version'],
    'limits' => [
        'video' => (int) $config['max_video_size'],
        'image' => (int) $config['max_image_size'],
        'audio' => (int) $config['max_audio_size'],
    ],
    'composition' => [
        'width' => (int) $config['composition_width'],
        'height' => (int) $config['composition_height'],
        'fps' => (int) $config['composition_fps'],
    ],
]);

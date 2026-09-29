<?php
declare(strict_types=1);

require_once dirname(__DIR__) . '/includes/bootstrap.php';
boot_api();
assert_post();
assert_same_origin();

$status = ffmpeg_status();
$project = validate_project(read_json_body());
if ($project['clips'] === [] && $project['images'] === [] && $project['texts'] === []) {
    throw new AppException('Add at least one video, image, or text element before exporting.');
}
if (!$status['ffmpeg'] || !$status['ffprobe']) {
    throw new AppException('FFmpeg is unavailable.');
}
if (!$status['phpCli']) {
    throw new AppException('PHP CLI was not found. Set php_cli in includes/config.local.php.');
}
if (!$status['writable']) {
    throw new AppException('Server storage is not available.', 500);
}

$ids = [];
foreach (['clips', 'images', 'audios'] as $key) {
    foreach ($project[$key] as $item) {
        $ids[] = (string) $item['mediaId'];
    }
}
library_for_export($ids);

$duration = project_duration($project);
if ($duration < 0.1) {
    throw new AppException('Add at least one video, image, or text element before exporting.');
}

$job = create_export_job($project, $duration);
try {
    launch_export_worker($job['id']);
} catch (AppException $e) {
    update_job($job['id'], static function (array $current) use ($e): array {
        $current['status'] = 'error';
        $current['error'] = $e->getMessage();
        $current['finishedAt'] = time();
        return $current;
    });
    throw $e;
}

json_response([
    'ok' => true,
    'message' => 'Export started',
    'job' => public_job(read_job($job['id']) ?? $job),
]);

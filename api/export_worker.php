<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit;
}

require_once dirname(__DIR__) . '/includes/bootstrap.php';
boot_api(false);
set_time_limit(0);
ignore_user_abort(true);

$jobId = $argv[1] ?? '';
if (!is_token($jobId)) {
    fwrite(STDERR, "Export job not found.\n");
    exit(1);
}

$job = read_job($jobId);
if ($job === null) {
    fwrite(STDERR, "Export job not found.\n");
    exit(1);
}

$fail = static function (string $message) use ($jobId): void {
    log_error('Export ' . $jobId . ' failed: ' . $message);
    try {
        update_job($jobId, static function (array $current) use ($message): array {
            $current['status'] = 'error';
            $current['error'] = $message;
            $current['finishedAt'] = time();
            return $current;
        });
    } catch (Throwable) {
        // The job file may already be unreadable.
    }
};

try {
    update_job($jobId, static function (array $current): array {
        $current['status'] = 'running';
        $current['startedAt'] = time();
        $current['pid'] = getmypid() ?: 0;
        $current['error'] = '';
        return $current;
    });

    $project = $job['project'] ?? null;
    if (!is_array($project)) {
        throw new AppException('Invalid project data.');
    }
    $ids = [];
    foreach (['clips', 'images', 'audios'] as $key) {
        foreach ($project[$key] ?? [] as $item) {
            if (is_array($item)) {
                $ids[] = (string) ($item['mediaId'] ?? '');
            }
        }
    }
    $library = library_for_export($ids);
    $dir = job_directory($jobId);
    $progress = $dir . DIRECTORY_SEPARATOR . 'progress.txt';
    $output = output_file($jobId);
    $log = $dir . DIRECTORY_SEPARATOR . 'ffmpeg.log';
    $compiled = compile_export($project, $library, $dir, $output, $progress);
    $code = run_process_logged($compiled['command'], $log);
    clearstatcache(true, $output);
    if ($code !== 0 || !is_file($output) || filesize($output) === 0) {
        if (is_file($output)) {
            @unlink($output);
        }
        $detail = is_file($log) ? (string) file_get_contents($log) : '';
        log_error('FFmpeg exit ' . $code . ' ' . $detail);
        throw new AppException('Export failed.');
    }

    update_job($jobId, static function (array $current): array {
        $current['status'] = 'done';
        $current['progress'] = 100;
        $current['finishedAt'] = time();
        $current['error'] = '';
        return $current;
    });
} catch (AppException $e) {
    $fail($e->getMessage());
    exit(1);
} catch (Throwable $e) {
    log_error($e->getMessage());
    $fail('Export failed.');
    exit(1);
}

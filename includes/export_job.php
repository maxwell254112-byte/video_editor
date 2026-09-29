<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli' && isset($_SERVER['SCRIPT_FILENAME']) && realpath((string) $_SERVER['SCRIPT_FILENAME']) === realpath(__FILE__)) {
    http_response_code(404);
    exit;
}

function job_directory(string $id): string
{
    if (!is_token($id)) {
        throw new AppException('Export job not found.', 404);
    }
    return app_config()['export_directory'] . DIRECTORY_SEPARATOR . 'jobs' . DIRECTORY_SEPARATOR . $id;
}

function job_file(string $id): string
{
    return job_directory($id) . DIRECTORY_SEPARATOR . 'job.json';
}

function output_file(string $id): string
{
    if (!is_token($id)) {
        throw new AppException('Export job not found.', 404);
    }
    return app_config()['export_directory'] . DIRECTORY_SEPARATOR . $id . '.mp4';
}

function read_job(string $id): ?array
{
    $path = job_file($id);
    if (!is_file($path)) {
        return null;
    }
    $handle = fopen($path, 'rb');
    if ($handle === false) {
        return null;
    }
    flock($handle, LOCK_SH);
    $raw = stream_get_contents($handle);
    flock($handle, LOCK_UN);
    fclose($handle);
    try {
        $data = json_decode((string) $raw, true, 32, JSON_THROW_ON_ERROR);
    } catch (JsonException) {
        return null;
    }
    return is_array($data) ? $data : null;
}

function write_job(string $id, array $job): void
{
    $dir = job_directory($id);
    if (!is_dir($dir) && !mkdir($dir, 0755, true) && !is_dir($dir)) {
        throw new AppException('Export failed.');
    }
    $path = job_file($id);
    $handle = fopen($path, 'c+');
    if ($handle === false) {
        throw new AppException('Export failed.');
    }
    flock($handle, LOCK_EX);
    rewind($handle);
    ftruncate($handle, 0);
    fwrite($handle, json_encode($job, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR));
    fflush($handle);
    flock($handle, LOCK_UN);
    fclose($handle);
}

function update_job(string $id, callable $mutator): array
{
    $job = read_job($id);
    if ($job === null) {
        throw new AppException('Export job not found.', 404);
    }
    $updated = $mutator($job);
    if (!is_array($updated)) {
        throw new AppException('Export failed.');
    }
    write_job($id, $updated);
    return $updated;
}

function create_export_job(array $project, float $duration): array
{
    cleanup_old_exports();
    $id = new_id();
    $safe = preg_replace('/[^\p{L}\p{N}\s._-]/u', '', (string) $project['name']) ?? '';
    $safe = trim((string) preg_replace('/\s+/', ' ', (string) $safe));
    if ($safe === '') {
        $safe = 'video';
    }
    if (function_exists('mb_substr')) {
        $safe = mb_substr($safe, 0, 60);
    }
    $job = [
        'id' => $id,
        'status' => 'queued',
        'progress' => 0,
        'error' => '',
        'createdAt' => time(),
        'startedAt' => 0,
        'finishedAt' => 0,
        'duration' => $duration,
        'pid' => 0,
        'downloadName' => $safe . '.mp4',
        'project' => $project,
    ];
    write_job($id, $job);
    return $job;
}

function launch_export_worker(string $id): void
{
    $php = php_cli_binary();
    if ($php === null) {
        throw new AppException('PHP CLI was not found. Set php_cli in includes/config.local.php.');
    }
    $worker = dirname(__DIR__) . DIRECTORY_SEPARATOR . 'api' . DIRECTORY_SEPARATOR . 'export_worker.php';
    if (!is_file($worker)) {
        throw new AppException('Export failed.');
    }
    spawn_detached([$php, $worker, $id]);
}

function process_is_running(int $pid): bool
{
    if ($pid <= 0) {
        return false;
    }
    if (PHP_OS_FAMILY === 'Windows') {
        $ran = run_process(['tasklist', '/FI', 'PID eq ' . $pid, '/NH'], 8);
        return str_contains($ran['stdout'], (string) $pid);
    }
    if (function_exists('posix_kill')) {
        return @posix_kill($pid, 0);
    }
    return is_dir('/proc/' . $pid);
}

function public_job(array $job): array
{
    $id = (string) $job['id'];
    $status = (string) ($job['status'] ?? 'queued');
    $duration = (float) ($job['duration'] ?? 0);
    $progress = (float) ($job['progress'] ?? 0);
    if ($status === 'running') {
        $progress = parse_ffmpeg_progress(job_directory($id) . DIRECTORY_SEPARATOR . 'progress.txt', $duration);
        $pid = (int) ($job['pid'] ?? 0);
        $started = (int) ($job['startedAt'] ?? 0);
        if ($pid > 0 && $started > 0 && (time() - $started) > 3 && !process_is_running($pid)) {
            $status = 'error';
            $job['error'] = $job['error'] !== '' ? $job['error'] : 'Export failed.';
            $job['status'] = 'error';
            $job['finishedAt'] = time();
            write_job($id, $job);
        }
    }
    if ($status === 'done') {
        $progress = 100;
    }
    $startedAt = (int) ($job['startedAt'] ?? 0);
    $finishedAt = (int) ($job['finishedAt'] ?? 0);
    $elapsed = 0;
    if ($startedAt > 0) {
        $elapsed = max(0, ($finishedAt > 0 ? $finishedAt : time()) - $startedAt);
    }
    return [
        'id' => $id,
        'status' => $status,
        'progress' => round($progress, 1),
        'error' => (string) ($job['error'] ?? ''),
        'elapsed' => $elapsed,
        'downloadUrl' => $status === 'done' ? 'api/download.php?job=' . rawurlencode($id) : '',
    ];
}

function cleanup_old_exports(): void
{
    $root = app_config()['export_directory'];
    $jobs = glob($root . DIRECTORY_SEPARATOR . 'jobs' . DIRECTORY_SEPARATOR . '*', GLOB_ONLYDIR) ?: [];
    $now = time();
    foreach ($jobs as $dir) {
        $id = basename($dir);
        if (!is_token($id)) {
            continue;
        }
        $job = read_job($id);
        $created = (int) ($job['createdAt'] ?? filemtime($dir));
        if (($now - $created) < 172800) {
            continue;
        }
        if (($job['status'] ?? '') === 'running') {
            continue;
        }
        foreach (glob($dir . DIRECTORY_SEPARATOR . '*') ?: [] as $file) {
            if (is_file($file)) {
                @unlink($file);
            }
        }
        @rmdir($dir);
        $video = $root . DIRECTORY_SEPARATOR . $id . '.mp4';
        if (is_file($video)) {
            @unlink($video);
        }
    }
}

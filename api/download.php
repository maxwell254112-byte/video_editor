<?php
declare(strict_types=1);

require_once dirname(__DIR__) . '/includes/bootstrap.php';
boot_api(false);

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'GET') {
    http_response_code(405);
    header('Content-Type: text/plain; charset=utf-8');
    echo 'Invalid request.';
    exit;
}

$id = (string) ($_GET['job'] ?? '');
$job = is_token($id) ? read_job($id) : null;
$file = is_token($id) ? output_file($id) : '';
if ($job === null || ($job['status'] ?? '') !== 'done' || !is_file($file)) {
    http_response_code(404);
    header('Content-Type: text/plain; charset=utf-8');
    echo 'Export job not found.';
    exit;
}

$name = (string) ($job['downloadName'] ?? 'video.mp4');
$name = str_replace(['"', "\r", "\n"], '', $name);
if ($name === '') {
    $name = 'video.mp4';
}

header('Content-Type: video/mp4');
header('X-Content-Type-Options: nosniff');
header('Content-Length: ' . (string) filesize($file));
header('Content-Disposition: attachment; filename="' . $name . '"');
header('Cache-Control: no-store');
readfile($file);
exit;

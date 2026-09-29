<?php
declare(strict_types=1);

require_once dirname(__DIR__) . '/includes/bootstrap.php';
boot_api();

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'GET') {
    throw new AppException('Invalid request.', 405);
}

$id = (string) ($_GET['job'] ?? '');
if (!is_token($id)) {
    throw new AppException('Export job not found.', 404);
}
$job = read_job($id);
if ($job === null) {
    throw new AppException('Export job not found.', 404);
}

json_response([
    'ok' => true,
    'message' => 'Export status',
    'job' => public_job($job),
]);

<?php
declare(strict_types=1);

require_once dirname(__DIR__) . '/includes/bootstrap.php';
boot_api();

$method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
if ($method === 'GET') {
    json_response([
        'ok' => true,
        'message' => 'Media library',
        'media' => list_media(),
    ]);
}

if ($method === 'DELETE' || $method === 'POST') {
    assert_same_origin();
    $id = (string) ($_GET['id'] ?? $_POST['id'] ?? '');
    if ($method === 'POST') {
        $body = $_POST;
        if ($body === []) {
            $raw = file_get_contents('php://input');
            if (is_string($raw) && $raw !== '') {
                try {
                    $decoded = json_decode($raw, true, 8, JSON_THROW_ON_ERROR);
                    if (is_array($decoded)) {
                        $body = $decoded;
                    }
                } catch (JsonException) {
                    $body = [];
                }
            }
        }
        $id = (string) ($body['id'] ?? $id);
        $action = (string) ($body['action'] ?? 'delete');
        if ($action !== 'delete') {
            throw new AppException('Invalid request.', 405);
        }
    }
    if (!is_token($id)) {
        throw new AppException('Media could not be found.', 404);
    }
    delete_media_files($id);
    json_response(['ok' => true, 'message' => 'Media deleted']);
}

throw new AppException('Invalid request.', 405);

<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli' && isset($_SERVER['SCRIPT_FILENAME']) && realpath((string) $_SERVER['SCRIPT_FILENAME']) === realpath(__FILE__)) {
    http_response_code(404);
    exit;
}

function media_subdir(string $type): string
{
    return match ($type) {
        'video' => 'videos',
        'image' => 'images',
        'audio' => 'audio',
        default => throw new AppException('Invalid media path.'),
    };
}

function save_media_meta(array $meta): void
{
    $id = (string) ($meta['id'] ?? '');
    if (!is_token($id)) {
        throw new AppException('Invalid media path.');
    }
    $path = app_config()['upload_directory'] . DIRECTORY_SEPARATOR . 'meta' . DIRECTORY_SEPARATOR . $id . '.json';
    $json = json_encode($meta, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
    if (file_put_contents($path, $json, LOCK_EX) === false) {
        throw new AppException('Upload failed.');
    }
}

function load_media_meta(string $id): ?array
{
    if (!is_token($id)) {
        return null;
    }
    $path = app_config()['upload_directory'] . DIRECTORY_SEPARATOR . 'meta' . DIRECTORY_SEPARATOR . $id . '.json';
    if (!is_file($path)) {
        return null;
    }
    try {
        $data = json_decode((string) file_get_contents($path), true, 16, JSON_THROW_ON_ERROR);
    } catch (JsonException) {
        return null;
    }
    return is_array($data) ? $data : null;
}

function absolute_media_path(array $meta): string
{
    $filename = $meta['id'] . '.' . $meta['extension'];
    return storage_file(app_config()['upload_directory'], media_subdir((string) $meta['type']), $filename);
}

function public_media(array $meta): array
{
    $id = (string) $meta['id'];
    $ext = (string) $meta['extension'];
    $subdir = media_subdir((string) $meta['type']);
    $uploads = app_config()['upload_directory'];
    $thumbFile = $uploads . DIRECTORY_SEPARATOR . 'thumbs' . DIRECTORY_SEPARATOR . $id . '.jpg';
    $previewFile = $uploads . DIRECTORY_SEPARATOR . 'previews' . DIRECTORY_SEPARATOR . $id . '.mp4';
    $url = 'uploads/' . $subdir . '/' . $id . '.' . $ext;
    $thumb = is_file($thumbFile) ? 'uploads/thumbs/' . $id . '.jpg' : null;
    if ($thumb === null && ($meta['type'] ?? '') === 'image') {
        $thumb = $url;
    }
    return [
        'id' => $id,
        'type' => $meta['type'],
        'name' => $meta['name'],
        'extension' => $ext,
        'mime' => $meta['mime'],
        'size' => (int) $meta['size'],
        'duration' => (float) ($meta['duration'] ?? 0),
        'width' => (int) ($meta['width'] ?? 0),
        'height' => (int) ($meta['height'] ?? 0),
        'hasAudio' => (bool) ($meta['hasAudio'] ?? false),
        'hasVideo' => (bool) ($meta['hasVideo'] ?? false),
        'createdAt' => (string) ($meta['createdAt'] ?? ''),
        'url' => $url,
        'thumbUrl' => $thumb,
        'previewUrl' => is_file($previewFile) ? 'uploads/previews/' . $id . '.mp4' : null,
    ];
}

function list_media(): array
{
    $dir = app_config()['upload_directory'] . DIRECTORY_SEPARATOR . 'meta';
    $items = [];
    foreach (glob($dir . DIRECTORY_SEPARATOR . '*.json') ?: [] as $file) {
        try {
            $data = json_decode((string) file_get_contents($file), true, 16, JSON_THROW_ON_ERROR);
        } catch (JsonException) {
            continue;
        }
        if (!is_array($data) || !isset($data['id'], $data['type'], $data['extension'])) {
            continue;
        }
        $items[] = public_media($data);
    }
    usort($items, static fn (array $a, array $b): int => strcmp($b['createdAt'], $a['createdAt']));
    return $items;
}

function delete_media_files(string $id): void
{
    $meta = load_media_meta($id);
    if ($meta === null) {
        throw new AppException('Media could not be found.', 404);
    }
    $uploads = app_config()['upload_directory'];
    $paths = [
        absolute_media_path($meta),
        $uploads . DIRECTORY_SEPARATOR . 'thumbs' . DIRECTORY_SEPARATOR . $id . '.jpg',
        $uploads . DIRECTORY_SEPARATOR . 'previews' . DIRECTORY_SEPARATOR . $id . '.mp4',
        $uploads . DIRECTORY_SEPARATOR . 'meta' . DIRECTORY_SEPARATOR . $id . '.json',
    ];
    foreach ($paths as $path) {
        if (is_file($path)) {
            @unlink($path);
        }
    }
}

/**
 * @param array<int, string> $ids
 * @return array<string, array<string, mixed>>
 */
function library_for_export(array $ids): array
{
    $library = [];
    foreach (array_unique($ids) as $id) {
        if (!is_token((string) $id)) {
            throw new AppException('Missing media.');
        }
        $meta = load_media_meta((string) $id);
        if ($meta === null) {
            throw new AppException('Missing media.');
        }
        $path = absolute_media_path($meta);
        if (!is_file($path)) {
            throw new AppException('Missing media.');
        }
        $meta['path'] = $path;
        $library[(string) $id] = $meta;
    }
    return $library;
}

<?php
declare(strict_types=1);

require_once dirname(__DIR__) . '/includes/bootstrap.php';
boot_api();
assert_post();
assert_same_origin();
set_time_limit(0);

if (empty($_FILES) && (int) ($_SERVER['CONTENT_LENGTH'] ?? 0) > 0) {
    throw new AppException('File is too large.');
}

$uploads = normalize_uploaded_files($_FILES['files'] ?? []);
if ($uploads === []) {
    throw new AppException('Upload failed.');
}
if (count($uploads) > 10) {
    throw new AppException('Upload at most 10 files at a time.');
}

$stored = [];
$errors = [];
foreach ($uploads as $file) {
    $moved = '';
    try {
        $checked = validate_uploaded_file($file);
        $id = new_id();
        $subdir = media_subdir($checked['mediaType']);
        $filename = $id . '.' . $checked['extension'];
        $destination = app_config()['upload_directory'] . DIRECTORY_SEPARATOR . $subdir . DIRECTORY_SEPARATOR . $filename;
        if (!move_uploaded_file($file['tmp_name'], $destination)) {
            throw new AppException('Upload failed.');
        }
        $moved = $destination;

        $duration = 0.0;
        $width = 0;
        $height = 0;
        $hasAudio = false;
        $hasVideo = false;
        $codec = '';

        if ($checked['mediaType'] === 'image') {
            $info = @getimagesize($destination);
            if ($info === false) {
                throw new AppException('File is not a valid image.');
            }
            $width = (int) $info[0];
            $height = (int) $info[1];
            $hasVideo = false;
        } else {
            $probe = probe_media($destination);
            if ($checked['mediaType'] === 'video' && !$probe['hasVideo']) {
                throw new AppException('Media could not be analysed.');
            }
            if ($checked['mediaType'] === 'audio' && !$probe['hasAudio']) {
                throw new AppException('Media could not be analysed.');
            }
            if ($probe['duration'] <= 0) {
                throw new AppException('Media could not be analysed.');
            }
            $duration = $probe['duration'];
            $width = $probe['width'];
            $height = $probe['height'];
            $hasAudio = $probe['hasAudio'];
            $hasVideo = $probe['hasVideo'];
            $codec = $probe['videoCodec'];
        }

        $thumb = app_config()['upload_directory'] . DIRECTORY_SEPARATOR . 'thumbs' . DIRECTORY_SEPARATOR . $id . '.jpg';
        if ($checked['mediaType'] !== 'audio') {
            make_thumbnail($destination, $thumb);
        }

        if ($checked['mediaType'] === 'video' && needs_browser_proxy($checked['extension'], $codec)) {
            $preview = app_config()['upload_directory'] . DIRECTORY_SEPARATOR . 'previews' . DIRECTORY_SEPARATOR . $id . '.mp4';
            if (!make_preview_proxy($destination, $preview)) {
                @unlink($preview);
            }
        }

        $meta = [
            'id' => $id,
            'type' => $checked['mediaType'],
            'name' => safe_filename((string) $file['name'], $checked['extension']),
            'extension' => $checked['extension'],
            'mime' => $checked['mime'],
            'size' => (int) $file['size'],
            'duration' => $duration,
            'width' => $width,
            'height' => $height,
            'hasAudio' => $hasAudio,
            'hasVideo' => $hasVideo,
            'videoCodec' => $codec,
            'createdAt' => gmdate('c'),
        ];
        save_media_meta($meta);
        $stored[] = public_media($meta);
    } catch (AppException $e) {
        if ($moved !== '' && is_file($moved)) {
            @unlink($moved);
        }
        $errors[] = safe_filename((string) ($file['name'] ?? 'file'), 'bin') . ': ' . $e->getMessage();
    }
}

if ($stored === []) {
    throw new AppException($errors[0] ?? 'Upload failed.');
}

json_response([
    'ok' => true,
    'message' => $errors === [] ? 'Upload successful' : 'Some files could not be uploaded.',
    'media' => $stored,
    'errors' => $errors,
]);

<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli' && isset($_SERVER['SCRIPT_FILENAME']) && realpath((string) $_SERVER['SCRIPT_FILENAME']) === realpath(__FILE__)) {
    http_response_code(404);
    exit;
}

function allowed_media_types(): array
{
    return [
        'mp4' => ['type' => 'video', 'mimes' => ['video/mp4', 'application/mp4']],
        'webm' => ['type' => 'video', 'mimes' => ['video/webm']],
        'mov' => ['type' => 'video', 'mimes' => ['video/quicktime', 'video/mp4', 'application/octet-stream']],
        'wmv' => ['type' => 'video', 'mimes' => ['video/x-ms-wmv', 'video/x-ms-asf', 'application/vnd.ms-asf', 'application/octet-stream']],
        'jpg' => ['type' => 'image', 'mimes' => ['image/jpeg']],
        'jpeg' => ['type' => 'image', 'mimes' => ['image/jpeg']],
        'png' => ['type' => 'image', 'mimes' => ['image/png']],
        'webp' => ['type' => 'image', 'mimes' => ['image/webp']],
        'mp3' => ['type' => 'audio', 'mimes' => ['audio/mpeg', 'audio/mp3']],
        'wav' => ['type' => 'audio', 'mimes' => ['audio/wav', 'audio/x-wav', 'audio/wave']],
        'aac' => ['type' => 'audio', 'mimes' => ['audio/aac', 'audio/x-aac', 'audio/vnd.dlna.adts']],
        'm4a' => ['type' => 'audio', 'mimes' => ['audio/mp4', 'audio/x-m4a', 'audio/m4a']],
    ];
}

function max_bytes_for(string $mediaType): int
{
    $config = app_config();
    return match ($mediaType) {
        'video' => (int) $config['max_video_size'],
        'image' => (int) $config['max_image_size'],
        'audio' => (int) $config['max_audio_size'],
        default => 0,
    };
}

function upload_error_message(int $code): string
{
    return match ($code) {
        UPLOAD_ERR_INI_SIZE, UPLOAD_ERR_FORM_SIZE => 'File is too large.',
        UPLOAD_ERR_PARTIAL, UPLOAD_ERR_NO_FILE => 'Upload failed.',
        default => 'Upload failed.',
    };
}

/**
 * @param array{name:string,type:string,tmp_name:string,error:int,size:int} $file
 * @return array{extension:string,mediaType:string,mime:string}
 */
function validate_uploaded_file(array $file): array
{
    $error = (int) ($file['error'] ?? UPLOAD_ERR_NO_FILE);
    if ($error !== UPLOAD_ERR_OK) {
        throw new AppException(upload_error_message($error));
    }
    $tmp = (string) ($file['tmp_name'] ?? '');
    if ($tmp === '' || !is_uploaded_file($tmp)) {
        throw new AppException('Upload failed.');
    }

    $extension = strtolower(pathinfo(basename(str_replace('\\', '/', (string) $file['name'])), PATHINFO_EXTENSION));
    $allowed = allowed_media_types();
    if (!isset($allowed[$extension])) {
        throw new AppException('Unsupported file type.');
    }

    $mediaType = $allowed[$extension]['type'];
    $size = (int) ($file['size'] ?? 0);
    if ($size <= 0) {
        throw new AppException('Upload failed.');
    }
    if ($size > max_bytes_for($mediaType)) {
        throw new AppException('File is too large.');
    }

    $finfo = new finfo(FILEINFO_MIME_TYPE);
    $mime = (string) $finfo->file($tmp);
    if (!in_array($mime, $allowed[$extension]['mimes'], true)) {
        throw new AppException('Unsupported file type.');
    }

    if ($mediaType === 'image') {
        $info = @getimagesize($tmp);
        if ($info === false) {
            throw new AppException('File is not a valid image.');
        }
    }

    return [
        'extension' => $extension,
        'mediaType' => $mediaType,
        'mime' => $mime,
    ];
}

/**
 * @return array<int, array{name:string,type:string,tmp_name:string,error:int,size:int}>
 */
function normalize_uploaded_files(array $files): array
{
    if (!isset($files['name'])) {
        return [];
    }
    if (!is_array($files['name'])) {
        return [[
            'name' => (string) $files['name'],
            'type' => (string) ($files['type'] ?? ''),
            'tmp_name' => (string) ($files['tmp_name'] ?? ''),
            'error' => (int) ($files['error'] ?? UPLOAD_ERR_NO_FILE),
            'size' => (int) ($files['size'] ?? 0),
        ]];
    }
    $list = [];
    foreach ($files['name'] as $index => $name) {
        $list[] = [
            'name' => (string) $name,
            'type' => (string) ($files['type'][$index] ?? ''),
            'tmp_name' => (string) ($files['tmp_name'][$index] ?? ''),
            'error' => (int) ($files['error'][$index] ?? UPLOAD_ERR_NO_FILE),
            'size' => (int) ($files['size'][$index] ?? 0),
        ];
    }
    return $list;
}

function require_list(mixed $value): array
{
    if (!is_array($value) || array_is_list($value) === false) {
        throw new AppException('Invalid project data.');
    }
    return $value;
}

function normalize_color(mixed $value): string
{
    $color = strtoupper(trim((string) $value));
    if (!preg_match('/^#[0-9A-F]{6}$/', $color)) {
        return '#FFFFFF';
    }
    return $color;
}

function normalize_client_id(mixed $value): string
{
    $id = (string) $value;
    if (!preg_match('/^[A-Za-z0-9_-]{1,64}$/', $id)) {
        throw new AppException('Invalid project data.');
    }
    return $id;
}

function normalize_crop(mixed $crop): array
{
    $source = is_array($crop) ? $crop : [];
    $out = [];
    foreach (['top', 'right', 'bottom', 'left'] as $edge) {
        $out[$edge] = clamp_float($source[$edge] ?? 0, 0, 40, 0);
    }
    if ($out['top'] + $out['bottom'] > 80) {
        $scale = 80 / ($out['top'] + $out['bottom']);
        $out['top'] *= $scale;
        $out['bottom'] *= $scale;
    }
    if ($out['left'] + $out['right'] > 80) {
        $scale = 80 / ($out['left'] + $out['right']);
        $out['left'] *= $scale;
        $out['right'] *= $scale;
    }
    return $out;
}

/**
 * @return array<string, mixed>
 */
function validate_project(array $input): array
{
    $config = app_config();
    $name = trim(strip_tags((string) ($input['name'] ?? 'Untitled Project')));
    $name = trim((string) preg_replace('/\s+/', ' ', $name));
    if ($name === '') {
        $name = 'Untitled Project';
    }
    if (function_exists('mb_substr')) {
        $name = mb_substr($name, 0, 80);
    } else {
        $name = substr($name, 0, 80);
    }

    $fps = clamp_int($input['fps'] ?? $config['composition_fps'], 24, 60, 30);
    if (!in_array($fps, [24, 25, 30, 50, 60], true)) {
        $fps = 30;
    }
    $width = clamp_int($input['width'] ?? $config['composition_width'], 320, 3840, 1920);
    $height = clamp_int($input['height'] ?? $config['composition_height'], 240, 2160, 1080);
    $width -= $width % 2;
    $height -= $height % 2;

    $filtersIn = is_array($input['filters'] ?? null) ? $input['filters'] : [];
    $filters = [
        'brightness' => clamp_float($filtersIn['brightness'] ?? 0, -100, 100, 0),
        'contrast' => clamp_float($filtersIn['contrast'] ?? 0, -100, 100, 0),
        'saturation' => clamp_float($filtersIn['saturation'] ?? 0, -100, 100, 0),
        'grayscale' => clamp_float($filtersIn['grayscale'] ?? 0, 0, 100, 0),
        'blur' => clamp_float($filtersIn['blur'] ?? 0, 0, 20, 0),
        'sepia' => clamp_float($filtersIn['sepia'] ?? 0, 0, 100, 0),
    ];

    $exportMode = (($input['exportMode'] ?? 'fast') === 'high') ? 'high' : 'fast';
    $fonts = ['Arial', 'Verdana', 'Georgia', 'Times New Roman', 'Courier New', 'Microsoft YaHei', 'SimHei', 'SimSun'];

    $clips = [];
    foreach (array_slice(require_list($input['clips'] ?? []), 0, 40) as $clip) {
        if (!is_array($clip)) {
            throw new AppException('Invalid project data.');
        }
        $in = clamp_float($clip['inPoint'] ?? 0, 0, 86400, 0);
        $source = clamp_float($clip['sourceDuration'] ?? 0, 0.1, 86400, 0.1);
        $out = clamp_float($clip['outPoint'] ?? $source, 0, $source, $source);
        if ($out <= $in) {
            throw new AppException('Invalid project data.');
        }
        $speed = clamp_float($clip['speed'] ?? 1, 0.25, 4, 1);
        if (($out - $in) / $speed < 0.1) {
            throw new AppException('Invalid project data.');
        }
        $clips[] = [
            'id' => normalize_client_id($clip['id'] ?? ''),
            'mediaId' => (string) ($clip['mediaId'] ?? ''),
            'name' => safe_filename((string) ($clip['name'] ?? 'Clip'), 'mp4'),
            'timelineStart' => clamp_float($clip['timelineStart'] ?? 0, 0, 86400, 0),
            'inPoint' => $in,
            'outPoint' => $out,
            'sourceDuration' => $source,
            'speed' => $speed,
            'volume' => clamp_float($clip['volume'] ?? 1, 0, 1, 1),
            'muted' => normalize_bool($clip['muted'] ?? false),
            'x' => clamp_float($clip['x'] ?? 0, -4000, 8000, 0),
            'y' => clamp_float($clip['y'] ?? 0, -4000, 8000, 0),
            'scale' => clamp_float($clip['scale'] ?? 1, 0.1, 4, 1),
            'rotation' => clamp_float($clip['rotation'] ?? 0, 0, 359, 0),
            'crop' => normalize_crop($clip['crop'] ?? []),
        ];
        if (!is_token($clips[array_key_last($clips)]['mediaId'])) {
            throw new AppException('Missing media.');
        }
    }

    $images = [];
    foreach (array_slice(require_list($input['images'] ?? []), 0, 40) as $image) {
        if (!is_array($image)) {
            throw new AppException('Invalid project data.');
        }
        $start = clamp_float($image['start'] ?? 0, 0, 86400, 0);
        $end = clamp_float($image['end'] ?? ($start + 5), 0, 86400, $start + 5);
        if ($end < $start + 0.1) {
            throw new AppException('Invalid project data.');
        }
        $images[] = [
            'id' => normalize_client_id($image['id'] ?? ''),
            'mediaId' => (string) ($image['mediaId'] ?? ''),
            'name' => safe_filename((string) ($image['name'] ?? 'Image'), 'png'),
            'x' => clamp_float($image['x'] ?? 0, -4000, 8000, 0),
            'y' => clamp_float($image['y'] ?? 0, -4000, 8000, 0),
            'width' => clamp_float($image['width'] ?? 480, 10, 3840, 480),
            'scale' => clamp_float($image['scale'] ?? 1, 0.1, 4, 1),
            'start' => $start,
            'end' => $end,
            'opacity' => clamp_float($image['opacity'] ?? 1, 0, 1, 1),
            'rotation' => clamp_float($image['rotation'] ?? 0, 0, 359, 0),
        ];
        if (!is_token($images[array_key_last($images)]['mediaId'])) {
            throw new AppException('Missing media.');
        }
    }

    $texts = [];
    foreach (array_slice(require_list($input['texts'] ?? []), 0, 40) as $text) {
        if (!is_array($text)) {
            throw new AppException('Invalid project data.');
        }
        $content = trim(str_replace("\0", '', (string) ($text['content'] ?? '')));
        if (function_exists('mb_substr')) {
            $content = mb_substr($content, 0, 200);
        } else {
            $content = substr($content, 0, 200);
        }
        $start = clamp_float($text['start'] ?? 0, 0, 86400, 0);
        $end = clamp_float($text['end'] ?? ($start + 5), 0, 86400, $start + 5);
        if ($end < $start + 0.1) {
            throw new AppException('Invalid project data.');
        }
        $family = (string) ($text['fontFamily'] ?? 'Arial');
        if (!in_array($family, $fonts, true)) {
            $family = 'Arial';
        }
        $align = (string) ($text['align'] ?? 'center');
        if (!in_array($align, ['left', 'center', 'right'], true)) {
            $align = 'center';
        }
        $texts[] = [
            'id' => normalize_client_id($text['id'] ?? ''),
            'content' => $content,
            'fontFamily' => $family,
            'fontSize' => clamp_float($text['fontSize'] ?? 64, 8, 400, 64),
            'color' => normalize_color($text['color'] ?? '#FFFFFF'),
            'bold' => normalize_bool($text['bold'] ?? false),
            'italic' => normalize_bool($text['italic'] ?? false),
            'align' => $align,
            'x' => clamp_float($text['x'] ?? ($width / 2), -4000, 8000, $width / 2),
            'y' => clamp_float($text['y'] ?? ($height / 2), -4000, 8000, $height / 2),
            'start' => $start,
            'end' => $end,
            'opacity' => clamp_float($text['opacity'] ?? 1, 0, 1, 1),
            'rotation' => clamp_float($text['rotation'] ?? 0, 0, 359, 0),
        ];
    }

    $audios = [];
    foreach (array_slice(require_list($input['audios'] ?? []), 0, 20) as $audio) {
        if (!is_array($audio)) {
            throw new AppException('Invalid project data.');
        }
        $in = clamp_float($audio['inPoint'] ?? 0, 0, 86400, 0);
        $source = clamp_float($audio['sourceDuration'] ?? 0, 0.1, 86400, 0.1);
        $out = clamp_float($audio['outPoint'] ?? $source, 0, $source, $source);
        if ($out < $in + 0.1) {
            throw new AppException('Invalid project data.');
        }
        $audios[] = [
            'id' => normalize_client_id($audio['id'] ?? ''),
            'mediaId' => (string) ($audio['mediaId'] ?? ''),
            'name' => safe_filename((string) ($audio['name'] ?? 'Audio'), 'mp3'),
            'timelineStart' => clamp_float($audio['timelineStart'] ?? 0, 0, 86400, 0),
            'inPoint' => $in,
            'outPoint' => $out,
            'sourceDuration' => $source,
            'volume' => clamp_float($audio['volume'] ?? 1, 0, 1, 1),
            'track' => clamp_int($audio['track'] ?? 1, 1, 4, 1),
        ];
        if (!is_token($audios[array_key_last($audios)]['mediaId'])) {
            throw new AppException('Missing media.');
        }
    }

    return [
        'name' => $name,
        'fps' => $fps,
        'width' => $width,
        'height' => $height,
        'clips' => $clips,
        'images' => $images,
        'texts' => $texts,
        'audios' => $audios,
        'filters' => $filters,
        'muteOriginal' => normalize_bool($input['muteOriginal'] ?? false),
        'exportMode' => $exportMode,
    ];
}

function project_duration(array $project): float
{
    $max = 0.0;
    foreach ($project['clips'] as $clip) {
        $max = max($max, $clip['timelineStart'] + ($clip['outPoint'] - $clip['inPoint']) / $clip['speed']);
    }
    foreach ($project['images'] as $image) {
        $max = max($max, $image['end']);
    }
    foreach ($project['texts'] as $text) {
        if (trim($text['content']) !== '') {
            $max = max($max, $text['end']);
        }
    }
    foreach ($project['audios'] as $audio) {
        $max = max($max, $audio['timelineStart'] + ($audio['outPoint'] - $audio['inPoint']));
    }
    return $max;
}

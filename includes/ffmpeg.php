<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli' && isset($_SERVER['SCRIPT_FILENAME']) && realpath((string) $_SERVER['SCRIPT_FILENAME']) === realpath(__FILE__)) {
    http_response_code(404);
    exit;
}

function find_tool(string $tool): ?string
{
    $config = app_config();
    $configured = $tool === 'ffprobe' ? (string) $config['ffprobe_path'] : (string) $config['ffmpeg_path'];
    $root = (string) $config['root'];
    $candidates = array_filter([
        $configured,
        $root . '/bin/' . $tool,
        $root . '/bin/' . $tool . '.exe',
        '/usr/bin/' . $tool,
        '/usr/local/bin/' . $tool,
        '/bin/' . $tool,
        'C:/ffmpeg/bin/' . $tool . '.exe',
        'C:/xampp/ffmpeg/bin/' . $tool . '.exe',
    ]);
    foreach ($candidates as $path) {
        $ready = prepare_tool($path);
        if ($ready !== null) {
            return $ready;
        }
    }
    if (!process_functions_available()) {
        return null;
    }
    $finder = PHP_OS_FAMILY === 'Windows' ? 'where.exe' : 'which';
    $found = run_process([$finder, $tool], 5);
    if ($found['code'] !== 0) {
        return null;
    }
    foreach (preg_split('/\R/', $found['stdout']) ?: [] as $line) {
        $line = trim($line);
        $ready = prepare_tool($line);
        if ($ready !== null) {
            return $ready;
        }
    }
    return null;
}

function prepare_tool(string $path): ?string
{
    if ($path === '' || !is_file($path)) {
        return null;
    }
    if (PHP_OS_FAMILY !== 'Windows' && !is_executable($path)) {
        @chmod($path, 0755);
    }
    return $path;
}

function ffmpeg_binary(): ?string
{
    static $path = false;
    if ($path === false) {
        $path = find_tool('ffmpeg');
    }
    return $path ?: null;
}

function ffprobe_binary(): ?string
{
    static $path = false;
    if ($path === false) {
        $path = find_tool('ffprobe');
    }
    return $path ?: null;
}

function ffmpeg_status(): array
{
    $ffmpeg = ffmpeg_binary();
    $ffprobe = ffprobe_binary();
    $version = '';
    if ($ffmpeg !== null && process_functions_available()) {
        $ran = run_process([$ffmpeg, '-version'], 8);
        if (preg_match('/ffmpeg version\s+(\S+)/i', $ran['stdout'], $match)) {
            $version = $match[1];
        }
    }
    $ready = $ffmpeg !== null && $ffprobe !== null && process_functions_available() && $version !== '';
    return [
        'ffmpeg' => $ffmpeg !== null && $version !== '',
        'ffprobe' => $ffprobe !== null,
        'phpCli' => php_cli_binary() !== null,
        'writable' => app_writable(),
        'version' => $ready || $version !== '' ? $version : '',
        'message' => $ready ? 'FFmpeg: Ready' : 'FFmpeg: Not Available',
    ];
}

/**
 * @return array{duration:float,width:int,height:int,hasVideo:bool,hasAudio:bool,videoCodec:string}
 */
function probe_media(string $path): array
{
    $ffprobe = ffprobe_binary();
    if ($ffprobe === null) {
        throw new AppException('FFprobe is unavailable.');
    }
    $ran = run_process([
        $ffprobe, '-v', 'quiet', '-print_format', 'json', '-show_format', '-show_streams', $path,
    ], 40);
    if ($ran['code'] !== 0 || trim($ran['stdout']) === '') {
        throw new AppException('Media could not be analysed.');
    }
    try {
        $data = json_decode($ran['stdout'], true, 32, JSON_THROW_ON_ERROR);
    } catch (JsonException) {
        throw new AppException('Media could not be analysed.');
    }
    $duration = (float) ($data['format']['duration'] ?? 0);
    $width = 0;
    $height = 0;
    $hasVideo = false;
    $hasAudio = false;
    $codec = '';
    foreach ($data['streams'] ?? [] as $stream) {
        if (!is_array($stream)) {
            continue;
        }
        if (($stream['codec_type'] ?? '') === 'video' && !$hasVideo) {
            $hasVideo = true;
            $width = (int) ($stream['width'] ?? 0);
            $height = (int) ($stream['height'] ?? 0);
            $codec = strtolower((string) ($stream['codec_name'] ?? ''));
        }
        if (($stream['codec_type'] ?? '') === 'audio') {
            $hasAudio = true;
        }
    }
    if ($duration <= 0 && $hasVideo) {
        $duration = 0;
    }
    return [
        'duration' => max(0, $duration),
        'width' => $width,
        'height' => $height,
        'hasVideo' => $hasVideo,
        'hasAudio' => $hasAudio,
        'videoCodec' => $codec,
    ];
}

function make_thumbnail(string $input, string $output): bool
{
    $ffmpeg = ffmpeg_binary();
    if ($ffmpeg === null) {
        return false;
    }
    $ran = run_process([
        $ffmpeg, '-y', '-hide_banner', '-ss', '0', '-i', $input,
        '-frames:v', '1', '-vf', 'scale=320:-2', $output,
    ], 40);
    return $ran['code'] === 0 && is_file($output);
}

function make_preview_proxy(string $input, string $output): bool
{
    $ffmpeg = ffmpeg_binary();
    if ($ffmpeg === null) {
        return false;
    }
    $code = run_process_logged([
        $ffmpeg, '-y', '-hide_banner', '-i', $input,
        '-vf', 'scale=1280:-2',
        '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '30',
        '-c:a', 'aac', '-b:a', '96k', '-movflags', '+faststart',
        $output,
    ], app_config()['log_directory'] . DIRECTORY_SEPARATOR . 'proxy.log');
    return $code === 0 && is_file($output) && filesize($output) > 0;
}

function needs_browser_proxy(string $extension, string $codec): bool
{
    if (in_array($extension, ['wmv', 'mov'], true)) {
        return true;
    }
    if ($extension === 'webm') {
        return false;
    }
    return !in_array($codec, ['h264', 'avc1', 'vp8', 'vp9', 'av1'], true);
}

function font_catalog(): array
{
    $windir = getenv('WINDIR') ?: 'C:/Windows';
    $fonts = str_replace('\\', '/', $windir) . '/Fonts';
    return [
        'Arial' => [
            'regular' => $fonts . '/arial.ttf',
            'bold' => $fonts . '/arialbd.ttf',
            'italic' => $fonts . '/ariali.ttf',
            'boldItalic' => $fonts . '/arialbi.ttf',
        ],
        'Verdana' => [
            'regular' => $fonts . '/verdana.ttf',
            'bold' => $fonts . '/verdanab.ttf',
            'italic' => $fonts . '/verdanai.ttf',
            'boldItalic' => $fonts . '/verdanaz.ttf',
        ],
        'Georgia' => [
            'regular' => $fonts . '/georgia.ttf',
            'bold' => $fonts . '/georgiab.ttf',
            'italic' => $fonts . '/georgiai.ttf',
            'boldItalic' => $fonts . '/georgiaz.ttf',
        ],
        'Times New Roman' => [
            'regular' => $fonts . '/times.ttf',
            'bold' => $fonts . '/timesbd.ttf',
            'italic' => $fonts . '/timesi.ttf',
            'boldItalic' => $fonts . '/timesbi.ttf',
        ],
        'Courier New' => [
            'regular' => $fonts . '/cour.ttf',
            'bold' => $fonts . '/courbd.ttf',
            'italic' => $fonts . '/couri.ttf',
            'boldItalic' => $fonts . '/courbi.ttf',
        ],
        'Microsoft YaHei' => [
            'regular' => $fonts . '/msyh.ttc',
            'bold' => $fonts . '/msyhbd.ttc',
            'italic' => $fonts . '/msyh.ttc',
            'boldItalic' => $fonts . '/msyhbd.ttc',
        ],
        'SimHei' => [
            'regular' => $fonts . '/simhei.ttf',
            'bold' => $fonts . '/simhei.ttf',
            'italic' => $fonts . '/simhei.ttf',
            'boldItalic' => $fonts . '/simhei.ttf',
        ],
        'SimSun' => [
            'regular' => $fonts . '/simsun.ttc',
            'bold' => $fonts . '/simsun.ttc',
            'italic' => $fonts . '/simsun.ttc',
            'boldItalic' => $fonts . '/simsun.ttc',
        ],
    ];
}

function resolve_font_file(string $family, bool $bold, bool $italic): ?string
{
    $catalog = font_catalog();
    $entry = $catalog[$family] ?? $catalog['Arial'];
    $key = $bold && $italic ? 'boldItalic' : ($bold ? 'bold' : ($italic ? 'italic' : 'regular'));
    $path = $entry[$key] ?? $entry['regular'];
    if (is_file($path)) {
        return $path;
    }
    if (is_file($entry['regular'])) {
        return $entry['regular'];
    }
    $fallbacks = [
        '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',
        '/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf',
        '/usr/share/fonts/dejavu/DejaVuSans.ttf',
    ];
    foreach ($fallbacks as $fallback) {
        if (is_file($fallback)) {
            return $fallback;
        }
    }
    return null;
}

function ff_escape_path(string $path): string
{
    $path = str_replace('\\', '/', $path);
    $path = str_replace(':', '\\:', $path);
    $path = str_replace("'", "\\'", $path);
    return $path;
}

function ff_between(float $start, float $end): string
{
    return "enable='between(t\\," . ff($start) . "\\," . ff($end) . ")'";
}

function atempo_chain(float $speed): string
{
    $parts = [];
    $guard = 0;
    while ($speed > 2.0 && $guard < 6) {
        $parts[] = 'atempo=2';
        $speed /= 2;
        $guard++;
    }
    while ($speed < 0.5 && $guard < 6) {
        $parts[] = 'atempo=0.5';
        $speed /= 0.5;
        $guard++;
    }
    $parts[] = 'atempo=' . ff($speed);
    return implode(',', $parts);
}

function rotation_filter(float $degrees): string
{
    $deg = (int) round($degrees) % 360;
    if ($deg < 0) {
        $deg += 360;
    }
    return match ($deg) {
        0 => '',
        90 => 'transpose=1',
        180 => 'hflip,vflip',
        270 => 'transpose=2',
        default => 'format=rgba,rotate=' . ff($degrees) . '*PI/180:c=none:ow=rotw(' . ff($degrees) . '*PI/180):oh=roth(' . ff($degrees) . '*PI/180)',
    };
}

function crop_filter(array $crop): string
{
    $left = $crop['left'] / 100;
    $right = $crop['right'] / 100;
    $top = $crop['top'] / 100;
    $bottom = $crop['bottom'] / 100;
    if ($left + $right + $top + $bottom < 0.001) {
        return '';
    }
    $cw = max(0.1, 1 - $left - $right);
    $ch = max(0.1, 1 - $top - $bottom);
    return "crop='trunc(iw*" . ff($cw) . "/2)*2':'trunc(ih*" . ff($ch) . "/2)*2':'iw*" . ff($left) . "':'ih*" . ff($top) . "'";
}

/**
 * @param array<string, array<string, mixed>> $library
 * @return array{command:array<int, string>, duration:float, filter:string, width:int, height:int}
 */
function compile_export(array $project, array $library, string $jobDir, string $output, string $progressFile): array
{
    $ffmpeg = ffmpeg_binary();
    if ($ffmpeg === null) {
        throw new AppException('FFmpeg is unavailable.');
    }

    $duration = round(project_duration($project), 3);
    if ($duration < 0.1) {
        throw new AppException('Add at least one video, image, or text element before exporting.');
    }

    $high = $project['exportMode'] === 'high';
    $outW = $high ? 1920 : 1280;
    $outH = $high ? 1080 : 720;
    $fps = (int) $project['fps'];
    $sx = $outW / max(1, (int) $project['width']);
    $sy = $outH / max(1, (int) $project['height']);

    $inputs = [];
    $chains = [];
    $audioLabels = [];
    $inputIndex = 1;

    $chains[] = '[0:v]format=yuv420p,fps=' . $fps . '[base0]';
    $current = 'base0';

    foreach ($project['clips'] as $clipIndex => $clip) {
        $media = $library[$clip['mediaId']] ?? null;
        if ($media === null || ($media['type'] ?? '') !== 'video') {
            throw new AppException('Missing media.');
        }
        $inputs[] = ['path' => $media['path'], 'loop' => false];
        $speed = (float) $clip['speed'];
        $start = (float) $clip['timelineStart'];
        $parts = ["[{$inputIndex}:v]"];
        $filters = [
            'trim=start=' . ff((float) $clip['inPoint']) . ':end=' . ff((float) $clip['outPoint']),
            'setpts=PTS-STARTPTS',
            'fps=' . $fps,
            "setpts='PTS/" . ff($speed) . "+" . ff($start) . "/TB'",
        ];
        $crop = crop_filter($clip['crop']);
        if ($crop !== '') {
            $filters[] = $crop;
        }
        $rotation = rotation_filter((float) $clip['rotation']);
        if ($rotation !== '') {
            $filters[] = $rotation;
        }
        $targetW = max(2, (int) round($outW * (float) $clip['scale']));
        $targetH = max(2, (int) round($outH * (float) $clip['scale']));
        $targetW -= $targetW % 2;
        $targetH -= $targetH % 2;
        $filters[] = 'scale=' . $targetW . ':' . $targetH . ':force_original_aspect_ratio=decrease';
        $filters[] = 'scale=trunc(iw/2)*2:trunc(ih/2)*2';
        $filters[] = 'setsar=1';
        $orthogonal = in_array(((int) round((float) $clip['rotation'])) % 360, [0, 90, 180, 270], true);
        $filters[] = $orthogonal ? 'format=yuv420p' : 'format=yuva420p';
        $label = 'cv' . $clipIndex;
        $chains[] = $parts[0] . implode(',', $filters) . '[' . $label . ']';
        $x = (int) round((float) $clip['x'] * $sx);
        $y = (int) round((float) $clip['y'] * $sy);
        $next = 'basev' . ($clipIndex + 1);
        $chains[] = "[{$current}][{$label}]overlay=x={$x}:y={$y}:eof_action=pass[{$next}]";
        $current = $next;

        $includeAudio = !empty($media['hasAudio'])
            && !$project['muteOriginal']
            && !$clip['muted']
            && (float) $clip['volume'] > 0.001;
        if ($includeAudio) {
            $aLabel = 'ca' . $clipIndex;
            $audio = [
                "[{$inputIndex}:a]atrim=start=" . ff((float) $clip['inPoint']) . ':end=' . ff((float) $clip['outPoint']),
                'asetpts=PTS-STARTPTS',
                atempo_chain($speed),
                'aresample=48000',
                'aformat=sample_fmts=fltp:channel_layouts=stereo',
                'volume=' . ff((float) $clip['volume']),
            ];
            $delay = (int) round($start * 1000);
            if ($delay > 0) {
                $audio[] = 'adelay=' . $delay . '|' . $delay;
            }
            $chains[] = implode(',', $audio) . '[' . $aLabel . ']';
            $audioLabels[] = $aLabel;
        }
        $inputIndex++;
    }

    foreach ($project['images'] as $imageIndex => $image) {
        $media = $library[$image['mediaId']] ?? null;
        if ($media === null || ($media['type'] ?? '') !== 'image') {
            throw new AppException('Missing media.');
        }
        $inputs[] = ['path' => $media['path'], 'loop' => true];
        $displayW = max(2, (int) round((float) $image['width'] * (float) $image['scale'] * $sx));
        $displayW -= $displayW % 2;
        $filters = [
            'scale=' . $displayW . ':-2',
            'format=rgba',
        ];
        if ((float) $image['opacity'] < 0.999) {
            $filters[] = 'colorchannelmixer=aa=' . ff((float) $image['opacity']);
        }
        $rotation = rotation_filter((float) $image['rotation']);
        if ($rotation !== '') {
            $filters[] = $rotation;
        }
        $label = 'im' . $imageIndex;
        $chains[] = "[{$inputIndex}:v]" . implode(',', $filters) . '[' . $label . ']';
        $mediaW = max(1, (int) ($media['width'] ?? 1));
        $mediaH = max(1, (int) ($media['height'] ?? 1));
        $displayH = max(2, (int) round($displayW * ($mediaH / $mediaW)));
        $displayH -= $displayH % 2;
        $radians = deg2rad((float) $image['rotation']);
        $boxW = abs($displayW * cos($radians)) + abs($displayH * sin($radians));
        $boxH = abs($displayW * sin($radians)) + abs($displayH * cos($radians));
        $centerX = ((float) $image['x'] * $sx) + ($displayW / 2);
        $centerY = ((float) $image['y'] * $sy) + ($displayH / 2);
        $x = (int) round($centerX - ($boxW / 2));
        $y = (int) round($centerY - ($boxH / 2));
        $next = 'basei' . $imageIndex;
        $chains[] = "[{$current}][{$label}]overlay=x={$x}:y={$y}:eof_action=pass:" . ff_between((float) $image['start'], (float) $image['end']) . "[{$next}]";
        $current = $next;
        $inputIndex++;
    }

    $textIndex = 0;
    foreach ($project['texts'] as $text) {
        $content = trim((string) $text['content']);
        if ($content === '') {
            continue;
        }
        $font = resolve_font_file((string) $text['fontFamily'], (bool) $text['bold'], (bool) $text['italic']);
        if ($font === null) {
            throw new AppException('A required font file could not be found on the server.');
        }
        $textFile = $jobDir . DIRECTORY_SEPARATOR . 'text-' . $textIndex . '.txt';
        if (file_put_contents($textFile, $content) === false) {
            throw new AppException('Export failed.');
        }
        $fontSize = max(8, (int) round((float) $text['fontSize'] * $sy));
        $anchorX = (float) $text['x'] * $sx;
        $anchorY = (float) $text['y'] * $sy;
        $color = ltrim((string) $text['color'], '#');
        $fontColor = '0x' . $color . '@' . ff((float) $text['opacity']);
        $fontIndex = str_ends_with(strtolower($font), '.ttc') ? ':fontindex=0' : '';
        $drawBase = "drawtext=fontfile='" . ff_escape_path($font) . "':textfile='" . ff_escape_path($textFile) . "':fontsize={$fontSize}:fontcolor={$fontColor}:expansion=none:line_spacing=6{$fontIndex}";
        $next = 'baset' . $textIndex;
        $rotation = ((int) round((float) $text['rotation'])) % 360;
        if ($rotation === 0) {
            $xExpr = match ($text['align']) {
                'center' => '(' . ff($anchorX) . '-text_w/2)',
                'right' => '(' . ff($anchorX) . '-text_w)',
                default => ff($anchorX),
            };
            $yExpr = '(' . ff($anchorY) . '-text_h/2)';
            $draw = $drawBase . ":x='{$xExpr}':y='{$yExpr}':" . ff_between((float) $text['start'], (float) $text['end']);
            $chains[] = "[{$current}]{$draw}[{$next}]";
        } else {
            $length = function_exists('mb_strlen') ? mb_strlen($content) : strlen($content);
            $estimate = (int) ceil($length * $fontSize * 1.2) + $fontSize;
            $boxW = max(64, $estimate * 2);
            $boxH = max(64, $fontSize * 4);
            $boxW -= $boxW % 2;
            $boxH -= $boxH % 2;
            $localX = match ($text['align']) {
                'center' => '(w-text_w)/2',
                'right' => '(w/2-text_w)',
                default => 'w/2',
            };
            $radians = deg2rad((float) $rotation);
            $rotatedW = abs($boxW * cos($radians)) + abs($boxH * sin($radians));
            $rotatedH = abs($boxW * sin($radians)) + abs($boxH * cos($radians));
            $overlayX = (int) round($anchorX - ($rotatedW / 2));
            $overlayY = (int) round($anchorY - ($rotatedH / 2));
            $spin = rotation_filter((float) $rotation);
            $label = 'tx' . $textIndex;
            $chains[] = 'color=c=black@0:s=' . $boxW . 'x' . $boxH . ':r=' . $fps . ':d=' . ff($duration)
                . ",format=rgba,{$drawBase}:x='{$localX}':y='(h-text_h)/2',{$spin}[{$label}]";
            $chains[] = "[{$current}][{$label}]overlay=x={$overlayX}:y={$overlayY}:eof_action=pass:"
                . ff_between((float) $text['start'], (float) $text['end']) . "[{$next}]";
        }
        $current = $next;
        $textIndex++;
    }

    $visual = visual_filters($project['filters']);
    if ($visual !== []) {
        $next = 'basef';
        $chains[] = "[{$current}]" . implode(',', $visual) . "[{$next}]";
        $current = $next;
    }
    if ((float) $project['filters']['sepia'] > 0.05) {
        $amount = ff(((float) $project['filters']['sepia']) / 100);
        $chains[] = "[{$current}]split[sepA][sepB]";
        $chains[] = '[sepB]colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131[sepC]';
        $chains[] = "[sepA][sepC]blend=all_expr='A*(1-{$amount})+B*{$amount}'[baseSep]";
        $current = 'baseSep';
    }
    $chains[] = "[{$current}]format=yuv420p[vout]";

    foreach ($project['audios'] as $audioIndex => $audioClip) {
        $media = $library[$audioClip['mediaId']] ?? null;
        if ($media === null || ($media['type'] ?? '') !== 'audio') {
            throw new AppException('Missing media.');
        }
        if ((float) $audioClip['volume'] <= 0.001) {
            continue;
        }
        $inputs[] = ['path' => $media['path'], 'loop' => false];
        $aLabel = 'bg' . $audioIndex;
        $audio = [
            "[{$inputIndex}:a]atrim=start=" . ff((float) $audioClip['inPoint']) . ':end=' . ff((float) $audioClip['outPoint']),
            'asetpts=PTS-STARTPTS',
            'aresample=48000',
            'aformat=sample_fmts=fltp:channel_layouts=stereo',
            'volume=' . ff((float) $audioClip['volume']),
        ];
        $delay = (int) round(((float) $audioClip['timelineStart']) * 1000);
        if ($delay > 0) {
            $audio[] = 'adelay=' . $delay . '|' . $delay;
        }
        $chains[] = implode(',', $audio) . '[' . $aLabel . ']';
        $audioLabels[] = $aLabel;
        $inputIndex++;
    }

    if ($audioLabels === []) {
        $chains[] = 'anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration=' . ff($duration) . ',asetpts=PTS-STARTPTS[aout]';
    } elseif (count($audioLabels) === 1) {
        $chains[] = '[' . $audioLabels[0] . ']anull[aout]';
    } else {
        $mixIn = '';
        foreach ($audioLabels as $label) {
            $mixIn .= '[' . $label . ']';
        }
        $chains[] = $mixIn . 'amix=inputs=' . count($audioLabels) . ':duration=longest:dropout_transition=0:normalize=0[aout]';
    }

    $filter = implode(';', $chains);
    $preset = $high ? 'medium' : 'veryfast';
    $crf = $high ? '18' : '26';
    $audioBitrate = $high ? '192k' : '160k';

    $command = [
        $ffmpeg, '-y', '-hide_banner',
        '-f', 'lavfi', '-i', 'color=c=black:s=' . $outW . 'x' . $outH . ':r=' . $fps . ':d=' . ff($duration),
    ];
    foreach ($inputs as $input) {
        if ($input['loop']) {
            $command[] = '-loop';
            $command[] = '1';
            $command[] = '-framerate';
            $command[] = (string) $fps;
        }
        $command[] = '-i';
        $command[] = $input['path'];
    }
    $filterFile = $jobDir . DIRECTORY_SEPARATOR . 'filter.txt';
    if (file_put_contents($filterFile, $filter) === false) {
        throw new AppException('Export failed.');
    }
    array_push(
        $command,
        '-filter_complex_script', $filterFile,
        '-map', '[vout]',
        '-map', '[aout]',
        '-t', ff($duration),
        '-r', (string) $fps,
        '-c:v', 'libx264',
        '-preset', $preset,
        '-crf', $crf,
        '-pix_fmt', 'yuv420p',
        '-c:a', 'aac',
        '-b:a', $audioBitrate,
        '-movflags', '+faststart',
        '-progress', $progressFile,
        '-nostats',
        $output
    );

    return [
        'command' => $command,
        'duration' => $duration,
        'filter' => $filter,
        'width' => $outW,
        'height' => $outH,
    ];
}

function visual_filters(array $filters): array
{
    $brightness = ((float) $filters['brightness']) / 100;
    $contrast = ((float) $filters['contrast'] + 100) / 100;
    $saturation = ((float) $filters['saturation'] + 100) / 100;
    $gray = ((float) $filters['grayscale']) / 100;
    $saturation *= (1 - $gray);
    $parts = [];
    if (abs($brightness) > 0.001 || abs($contrast - 1) > 0.001 || abs($saturation - 1) > 0.001) {
        $parts[] = 'eq=brightness=' . ff($brightness) . ':contrast=' . ff($contrast) . ':saturation=' . ff($saturation);
    }
    if ((float) $filters['blur'] > 0.05) {
        $parts[] = 'gblur=sigma=' . ff(min(20, (float) $filters['blur']));
    }
    return $parts;
}

function parse_ffmpeg_progress(string $progressFile, float $duration): float
{
    if (!is_file($progressFile) || $duration <= 0) {
        return 0.0;
    }
    $size = filesize($progressFile);
    $handle = fopen($progressFile, 'rb');
    if ($handle === false) {
        return 0.0;
    }
    if ($size > 8192) {
        fseek($handle, -8192, SEEK_END);
    }
    $tail = (string) fread($handle, 8192);
    fclose($handle);
    if (str_contains($tail, 'progress=end')) {
        return 100.0;
    }
    if (!preg_match_all('/out_time=(\d+):(\d+):(\d+(?:\.\d+)?)/', $tail, $matches, PREG_SET_ORDER)) {
        return 0.0;
    }
    $last = $matches[array_key_last($matches)];
    $seconds = ((int) $last[1]) * 3600 + ((int) $last[2]) * 60 + (float) $last[3];
    return max(0, min(99, ($seconds / $duration) * 100));
}

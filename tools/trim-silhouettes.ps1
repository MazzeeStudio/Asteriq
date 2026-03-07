# trim-silhouettes.ps1
# Trims transparent padding from device silhouette PNGs and renames them.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\trim-silhouettes.ps1
#
# Output goes to: src\Asteriq\Images\Devices\
# Review the $Mappings table below — remove any rows you don't want copied.

param(
    [int]$Padding = 20,   # transparent padding to leave around device (px)
    [switch]$DryRun       # print what would happen without writing files
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Runtime.InteropServices

$RepoRoot  = Split-Path $PSScriptRoot
$ViirpilDir = Join-Path $RepoRoot "mockups\virpil-no-background"
$VkbDir    = Join-Path $RepoRoot "mockups\vkb-no-background"
$OutputDir  = Join-Path $RepoRoot "src\Asteriq\Images\Devices"

# ---------------------------------------------------------------------------
# Mapping table: SourceDir | SourceFile | OutputFile
# Comment out / delete rows you don't want. Pick one when there are duplicates.
# ---------------------------------------------------------------------------
$Mappings = @(
    # ── Virpil Grips ───────────────────────────────────────────────────────
    @{ Dir=$ViirpilDir; Src="s6qzPfKnp6OA2VmImT_CWehgIgIQnxRjbQ-Photoroom.png";           Out="virpil_constellation_alpha_r.png" }
    # duplicate: s6qzPfKnp6OA2VmImT_CWehgIgIQnxRjbQ-Photoroom (1).png — skipped
    @{ Dir=$ViirpilDir; Src="5JMqKMDcGTu60P78snKLIfWE0Bv2YhVUSw-Photoroom.png";           Out="virpil_constellation_alpha_prime_r.png" }
    @{ Dir=$ViirpilDir; Src="Mc3eppMIqO8sYgjKH2sRgVovyhnl6x17ZA-Photoroom.png";           Out="virpil_constellation_alpha_prime_r_b.png" }
    @{ Dir=$ViirpilDir; Src="N1Hl7HU3nJTfxsAAANK5lUm5FlQToVnIzw-Photoroom.png";           Out="virpil_alpha_grip.png" }

    # ── Virpil Bases ───────────────────────────────────────────────────────
    @{ Dir=$ViirpilDir; Src="JWlabW1Ajo9jmr3AwmBRp14dAIntTuEcAA-Photoroom.png";           Out="virpil_mongoost50cm2_base.png" }
    @{ Dir=$ViirpilDir; Src="yGkLplLHMYzpM3q-bOqtirXlacIYo3uQnQ-Photoroom.png";           Out="virpil_warbrd_base.png" }

    # ── Virpil Throttles ───────────────────────────────────────────────────
    @{ Dir=$ViirpilDir; Src="DnHPqKLM7CjYbyJBICBgML_Cbc_w-dNZzA-Photoroom.png";           Out="virpil_mongoost50cm2_throttle.png" }
    @{ Dir=$ViirpilDir; Src="boqm7PtnwpS8-ckxOe_wx_Gw9dF5vux1yA-Photoroom.png";           Out="virpil_mongoost50cm2_throttle_b.png" }
    @{ Dir=$ViirpilDir; Src="iOziVrRGgehlo1R2IP_uJYT6H466mZKIvg-Photoroom.png";            Out="virpil_mongoost50cm2_throttle_3lev.png" }
    @{ Dir=$ViirpilDir; Src="2jDkLM9EKyw8cjA4jb-cZL0QA-TRLcEG5Q-Photoroom.png";           Out="virpil_throttle_cm2_grip.png" }
    @{ Dir=$ViirpilDir; Src="Q9M1X0xbXb6BnrELRTIPfpAM3zPCdz-1Kg-Photoroom.png";           Out="virpil_throttle_cm3_grip.png" }
    @{ Dir=$ViirpilDir; Src="Rz1hRbFRoibROdVI3GmzXguf69uN32-oHQ-Photoroom.png";            Out="virpil_throttle_stick_panel.png" }
    @{ Dir=$ViirpilDir; Src="dmkw3tTyB20aGIRRFvhgvY-pOi8KxA9gKA-Photoroom.png";            Out="virpil_ace2_throttle.png" }

    # ── Virpil Control Panels ──────────────────────────────────────────────
    @{ Dir=$ViirpilDir; Src="Oe_Z4nAo_XAchK97XqIvDh3KT3nRmYshFA-Photoroom.png";            Out="virpil_control_panel_1.png" }
    @{ Dir=$ViirpilDir; Src="WVI2b6Gq7qJR8cNb5itCxaAZDwKgnIY5eQ-Photoroom.png";            Out="virpil_control_panel_2.png" }

    # ── Virpil Rudder Pedals ───────────────────────────────────────────────
    @{ Dir=$ViirpilDir; Src="1LVil4dxlTzGeEF0LTe6Aeuy1NkPQj0k4g-Photoroom.png";           Out="virpil_sharka50.png" }
    # duplicate angle: PNy5s8mEC-iFyqfh9s4aBmBrxwdQwbU6-Q — skipped
    @{ Dir=$ViirpilDir; Src="4gptWtCYAumIOmO9bwmBLNoKSutL95Ei1A-Photoroom.png";            Out="virpil_sharka50_heels.png" }
    # duplicate angle: QR_ekcd4JLOu_d92nb6Ck2BsD2X8Fn5j9g — skipped
    @{ Dir=$ViirpilDir; Src="pufNZ9HEE6KrQh9bcL-l0kvJIKPYFpocOA-Photoroom.png";            Out="virpil_sharka50_compact.png" }
    @{ Dir=$ViirpilDir; Src="HOBFWNvNBw2MUv_Ww9kbbOY2ZRgQUtqeIw-Photoroom.png";            Out="virpil_ace_torq6.png" }
    @{ Dir=$ViirpilDir; Src="IDRuabVz4aeY96GpdxFhLJiNXqOuEPqMtw-Photoroom.png";            Out="virpil_ace_torq6_front.png" }
    @{ Dir=$ViirpilDir; Src="yke6Qio7KfFWIgjiy6Ie8Ihzb9e0tFoxUw-Photoroom.png";            Out="virpil_ace_torq6_b.png" }
    @{ Dir=$ViirpilDir; Src="sTTczVmyqnQeibXBetUUlb3P_-k5_YfMJw-Photoroom.png";            Out="virpil_ace2_pedals.png" }
    @{ Dir=$ViirpilDir; Src="uXtyhsjWfJ-feVI3T8knyaGJAs4zh7tQqQ-Photoroom.png";            Out="virpil_rudder_mk4.png" }
    @{ Dir=$ViirpilDir; Src="-rmXAONWw2lesLVFQf9kr4KQEHzlBWBkyw-Photoroom.png";            Out="virpil_rudder_compact.png" }

    # ── VKB ────────────────────────────────────────────────────────────────
    @{ Dir=$VkbDir; Src="67_GNX-EVOSCG-L_800_3-Photoroom.png";           Out="vkb_gnx_evo_scg_l.png" }
    @{ Dir=$VkbDir; Src="68_GNX-EVOSCG-R_800_2-Photoroom.png";           Out="vkb_gnx_evo_scg_r.png" }
    @{ Dir=$VkbDir; Src="70_GNX-EVOSCG-P_R_800_1-Photoroom.png";         Out="vkb_gnx_evo_scg_premium_r.png" }
    @{ Dir=$VkbDir; Src="74_GNX-EVOOMNI-THRT-R-P_800_1-Photoroom.png";   Out="vkb_gnx_evo_omni_throttle_r.png" }
    # duplicate angle: 74_GNX-EVOOMNI-THRT-R-P_800_2 — skipped
    @{ Dir=$VkbDir; Src="MCG-captions-3-Photoroom.png";                  Out="vkb_mcg.png" }
    @{ Dir=$VkbDir; Src="MCGU-4-Photoroom.png";                          Out="vkb_mcgu.png" }
    @{ Dir=$VkbDir; Src="SCG-R-P_For_GNE_1200_02-Photoroom.png";         Out="vkb_scg_r_premium.png" }
    @{ Dir=$VkbDir; Src="STECS-Mini-STG-L_04_1200-Photoroom.png";        Out="vkb_stecs_mini_l.png" }
    @{ Dir=$VkbDir; Src="STECS-Mini-STG-R_04_1200-Photoroom.png";        Out="vkb_stecs_mini_r.png" }
    @{ Dir=$VkbDir; Src="STECS-MiniP-STG-R_04_1200-Photoroom.png";       Out="vkb_stecs_mini_premium_r.png" }
    @{ Dir=$VkbDir; Src="STECS-Standard-STG-R_04_1200-Photoroom.png";    Out="vkb_stecs_standard_r.png" }
    @{ Dir=$VkbDir; Src="T-Rudder5_1200_01-Photoroom.png";               Out="vkb_t_rudder_mk5.png" }
)

# ---------------------------------------------------------------------------

function Invoke-TrimPng {
    param([string]$InputPath, [string]$OutputPath, [int]$Pad)

    $bmp = [System.Drawing.Bitmap]::new($InputPath)
    $w = $bmp.Width; $h = $bmp.Height

    # Read pixel data via LockBits (fast)
    $bd = $bmp.LockBits(
        [System.Drawing.Rectangle]::new(0, 0, $w, $h),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $bd.Stride
    $bytes = [byte[]]::new($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($bd)

    # Find tight content bounds
    $minX = $w; $maxX = 0; $minY = $h; $maxY = 0
    for ($y = 0; $y -lt $h; $y++) {
        $base = $y * $stride
        for ($x = 0; $x -lt $w; $x++) {
            if ($bytes[$base + $x * 4 + 3] -gt 10) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    $bmp.Dispose()

    if ($maxX -lt $minX) { Write-Warning "  No visible content - skipping"; return }

    $l = [Math]::Max(0, $minX - $Pad)
    $t = [Math]::Max(0, $minY - $Pad)
    $r = [Math]::Min($w - 1, $maxX + $Pad)
    $b = [Math]::Min($h - 1, $maxY + $Pad)
    $cw = $r - $l + 1; $ch = $b - $t + 1

    # Crop
    $orig = [System.Drawing.Bitmap]::new($InputPath)
    $out  = [System.Drawing.Bitmap]::new($cw, $ch, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g    = [System.Drawing.Graphics]::FromImage($out)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.DrawImage($orig,
        [System.Drawing.Rectangle]::new(0, 0, $cw, $ch),
        [System.Drawing.Rectangle]::new($l, $t, $cw, $ch),
        [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $orig.Dispose()

    $out.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
}

# ---------------------------------------------------------------------------

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$ok = 0; $skip = 0; $err = 0
foreach ($m in $Mappings) {
    $src = Join-Path $m.Dir $m.Src
    $dst = Join-Path $OutputDir $m.Out

    if (-not (Test-Path $src)) {
        Write-Warning "NOT FOUND: $($m.Src)"
        $skip++
        continue
    }

    if ($DryRun) {
        Write-Host "[dry-run] $($m.Src) -> $($m.Out)"
        $ok++
        continue
    }

    try {
        Write-Host -NoNewline "  $($m.Out) ... "
        Invoke-TrimPng -InputPath $src -OutputPath $dst -Pad $Padding
        # Print dimensions of output
        $info = [System.Drawing.Bitmap]::new($dst)
        Write-Host "$($info.Width)x$($info.Height)"
        $info.Dispose()
        $ok++
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
        $err++
    }
}

Write-Host ""
Write-Host "Done. $ok processed, $skip skipped, $err errors."
if (-not $DryRun) {
    Write-Host "Output: $OutputDir"
}

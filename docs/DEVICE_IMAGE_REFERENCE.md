# Device Image Reference

Silhouette images for Virpil and VKB devices.
Images go in `src/Asteriq/Images/Devices/` and are referenced from device map JSON files via the `"svgFile"` field.

Use `tools/trim-silhouettes.ps1` to trim transparent padding and rename files in one pass.

---

## Virpil

**Manufacturer VID:** `0x3344`
**PIDs:** Verify on actual hardware — use USBDeview or HID Device Viewer while device is connected.

### Joystick Grips

| Suggested filename | Device | Source file (hash) | Notes |
|--------------------|--------|--------------------|-------|
| `virpil_constellation_alpha_r.png` | VPC Constellation Alpha (R) | `s6qzPfKnp6OA2VmImT_CWehgIgIQnxRjbQ-Photoroom.png` | Keep one; duplicate below |
| _(duplicate)_ | VPC Constellation Alpha (R) | `s6qzPfKnp6OA2VmImT_CWehgIgIQnxRjbQ-Photoroom (1).png` | Same view — discard |
| `virpil_constellation_alpha_prime_r.png` | VPC Constellation Alpha Prime (R) | `5JMqKMDcGTu60P78snKLIfWE0Bv2YhVUSw-Photoroom.png` | 4 round thumb buttons |
| `virpil_constellation_alpha_prime_r_b.png` | VPC Constellation Alpha Prime (R) alt | `Mc3eppMIqO8sYgjKH2sRgVovyhnl6x17ZA-Photoroom.png` | Different angle — keep if preferred |
| `virpil_alpha_grip.png` | VPC Alpha / Delta Grip | `N1Hl7HU3nJTfxsAAANK5lUm5FlQToVnIzw-Photoroom.png` | More angular/tactical style |

### Joystick Bases

| Suggested filename | Device | Source file (hash) | Notes |
|--------------------|--------|--------------------|-------|
| `virpil_mongoost50cm2_base.png` | VPC MongoosT-50CM2 Base | `JWlabW1Ajo9jmr3AwmBRp14dAIntTuEcAA-Photoroom.png` | Base only, no grip |
| `virpil_warbrd_base.png` | VPC WarBRD-D Base | `yGkLplLHMYzpM3q-bOqtirXlacIYo3uQnQ-Photoroom.png` | Front view of base |

### Throttles

| Suggested filename | Device | Source file (hash) | Notes |
|--------------------|--------|--------------------|-------|
| `virpil_mongoost50cm2_throttle.png` | VPC MongoosT-50CM2 Throttle | `DnHPqKLM7CjYbyJBICBgML_Cbc_w-dNZzA-Photoroom.png` | Full base + handle |
| `virpil_mongoost50cm2_throttle_b.png` | VPC MongoosT-50CM2 Throttle (alt) | `boqm7PtnwpS8-ckxOe_wx_Gw9dF5vux1yA-Photoroom.png` | Different angle |
| `virpil_mongoost50cm2_throttle_3lev.png` | VPC MongoosT-50CM2 Throttle (3-lever) | `iOziVrRGgehlo1R2IP_uJYT6H466mZKIvg-Photoroom.png` | Three-lever configuration |
| `virpil_throttle_cm2_grip.png` | VPC Throttle CM2 Grip | `2jDkLM9EKyw8cjA4jb-cZL0QA-TRLcEG5Q-Photoroom.png` | Handle only |
| `virpil_throttle_cm3_grip.png` | VPC Throttle CM3 Grip | `Q9M1X0xbXb6BnrELRTIPfpAM3zPCdz-1Kg-Photoroom.png` | Taller handle, more controls |
| `virpil_throttle_stick_panel.png` | VPC Throttle with Stick Panel | `Rz1hRbFRoibROdVI3GmzXguf69uN32-oHQ-Photoroom.png` | Box + side stick |
| `virpil_ace2_throttle.png` | VPC ACE-2 Throttle | `dmkw3tTyB20aGIRRFvhgvY-pOi8KxA9gKA-Photoroom.png` | Wider base with separate stick |

### Control Panels

| Suggested filename | Device | Source file (hash) | Notes |
|--------------------|--------|--------------------|-------|
| `virpil_control_panel_1.png` | VPC Control Panel #1 (CP1) | `Oe_Z4nAo_XAchK97XqIvDh3KT3nRmYshFA-Photoroom.png` | Small square button box |
| `virpil_control_panel_2.png` | VPC Control Panel #2 (CP2) | `WVI2b6Gq7qJR8cNb5itCxaAZDwKgnIY5eQ-Photoroom.png` | Larger panel, two levers |

### Rudder Pedals

| Suggested filename | Device | Source file (hash) | Notes |
|--------------------|--------|--------------------|-------|
| `virpil_sharka50.png` | VPC SharKa-50 Rudder Pedals | `1LVil4dxlTzGeEF0LTe6Aeuy1NkPQj0k4g-Photoroom.png` | Round cutout pads, no heel rests |
| `virpil_sharka50_b.png` | VPC SharKa-50 (alt angle) | `PNy5s8mEC-iFyqfh9s4aBmBrxwdQwbU6-Q-Photoroom.png` | Keep one preferred view |
| `virpil_sharka50_heels.png` | VPC SharKa-50 + Heel Rests | `4gptWtCYAumIOmO9bwmBLNoKSutL95Ei1A-Photoroom.png` | With optional heel brackets |
| `virpil_sharka50_heels_b.png` | VPC SharKa-50 + Heel Rests (alt) | `QR_ekcd4JLOu_d92nb6Ck2BsD2X8Fn5j9g-Photoroom.png` | Duplicate — pick one |
| `virpil_sharka50_compact.png` | VPC SharKa-50 (compact/angle) | `pufNZ9HEE6KrQh9bcL-l0kvJIKPYFpocOA-Photoroom.png` | Different perspective |
| `virpil_ace_torq6_a.png` | VPC ACE (Torq6) Rudder Pedals | `HOBFWNvNBw2MUv_Ww9kbbOY2ZRgQUtqeIw-Photoroom.png` | Flat rectangular pads, side view |
| `virpil_ace_torq6_b.png` | VPC ACE (Torq6) Rudder Pedals | `IDRuabVz4aeY96GpdxFhLJiNXqOuEPqMtw-Photoroom.png` | Front view |
| `virpil_ace_torq6_c.png` | VPC ACE (Torq6) Rudder Pedals | `yke6Qio7KfFWIgjiy6Ie8Ihzb9e0tFoxUw-Photoroom.png` | Third angle |
| `virpil_ace2_pedals.png` | VPC ACE-2 Rudder Pedals | `sTTczVmyqnQeibXBetUUlb3P_-k5_YfMJw-Photoroom.png` | X-pattern four-contact points |
| `virpil_rudder_mk4.png` | VPC Rudder Mk.IV | `uXtyhsjWfJ-feVI3T8knyaGJAs4zh7tQqQ-Photoroom.png` | Very compact, two side arms |
| `virpil_rudder_compact.png` | VPC Rudder (compact bar) | `-rmXAONWw2lesLVFQf9kr4KQEHzlBWBkyw-Photoroom.png` | Horizontal bar style |

---

## VKB

**Manufacturer VID:** `0x231D` (most products) — some newer models may differ
**PIDs:** Verify on actual hardware.

| Suggested filename | Device | Source file | Notes |
|--------------------|--------|-------------|-------|
| `vkb_gnx_evo_scg_l.png` | VKB Gladiator NXT EVO SCG (Left) | `67_GNX-EVOSCG-L_800_3-Photoroom.png` | Full joystick, left-hand |
| `vkb_gnx_evo_scg_r.png` | VKB Gladiator NXT EVO SCG (Right) | `68_GNX-EVOSCG-R_800_2-Photoroom.png` | Full joystick, right-hand |
| `vkb_gnx_evo_scg_premium_r.png` | VKB Gladiator NXT EVO SCG Premium (R) | `70_GNX-EVOSCG-P_R_800_1-Photoroom.png` | Premium variant |
| `vkb_gnx_evo_omni_throttle_r.png` | VKB GNX EVO Omni Throttle (Right) | `74_GNX-EVOOMNI-THRT-R-P_800_1-Photoroom.png` | Throttle unit |
| `vkb_gnx_evo_omni_throttle_r_b.png` | VKB GNX EVO Omni Throttle (Right) alt | `74_GNX-EVOOMNI-THRT-R-P_800_2-Photoroom.png` | Different angle — pick one |
| `vkb_mcg.png` | VKB MCG (Military Combat Grip) | `MCG-captions-3-Photoroom.png` | Grip only, no base |
| `vkb_mcgu.png` | VKB MCGU (MCG Ultimate) | `MCGU-4-Photoroom.png` | On desktop base |
| `vkb_scg_r_premium.png` | VKB SCG-R Premium (for GNX EVO) | `SCG-R-P_For_GNE_1200_02-Photoroom.png` | Space Combat Grip |
| `vkb_stecs_mini_l.png` | VKB STECS Mini STG-L (Left) | `STECS-Mini-STG-L_04_1200-Photoroom.png` | Throttle, left-hand |
| `vkb_stecs_mini_r.png` | VKB STECS Mini STG-R (Right) | `STECS-Mini-STG-R_04_1200-Photoroom.png` | Throttle, right-hand |
| `vkb_stecs_mini_premium_r.png` | VKB STECS Mini Premium STG-R | `STECS-MiniP-STG-R_04_1200-Photoroom.png` | Premium mini, right-hand |
| `vkb_stecs_standard_r.png` | VKB STECS Standard STG-R | `STECS-Standard-STG-R_04_1200-Photoroom.png` | Standard size throttle |
| `vkb_t_rudder_mk5.png` | VKB T-Rudder Mk.5 | `T-Rudder5_1200_01-Photoroom.png` | Rudder pedals |

---

## VID:PID Notes

Device map JSON uses `"vidPid": "VID_XXXX&PID_YYYY"` format. To find the PID for a connected device:

1. Open **Device Manager** → Universal Serial Bus devices (or HID)
2. Right-click the device → Properties → Details → Hardware IDs
3. Note `VID_XXXX&PID_YYYY` values

Or use **USBDeview** (NirSoft) / **HID Device Viewer** for a cleaner view.

**Known Virpil VID:** `3344`
**Known VKB VID:** `231D`

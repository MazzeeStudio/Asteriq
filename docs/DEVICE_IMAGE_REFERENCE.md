# Device Image Reference

Silhouette images and device map JSON files for Virpil and VKB hardware.
Images live in `src/Asteriq/Images/Devices/`.
Device maps live in `src/Asteriq/Images/Devices/Maps/`.

When a device is plugged in, Asteriq matches its VID:PID to a device map JSON, which points to the silhouette image.

---

## Virpil — VID `0x3344`

| Device | VID:PID | Map JSON | Image |
|--------|---------|----------|-------|
| VPC MongoosT-50CM2 Base | 3344:0401 | virpil_mongoost50cm2_base.json | virpil_mongoost50cm2_base.png (925x933) |
| VPC WarBRD Base | 3344:0402 | virpil_warbrd_base.json | virpil_warbrd_base.png (1190x471) |
| VPC WarBRD-D Base | 3344:0403 | virpil_warbrdd_base.json | virpil_warbrd_base.png (shared) |
| VPC MongoosT-50CM3 Throttle | 3344:0501 | virpil_mongoost50cm3_throttle.json | virpil_mongoost50cm2_throttle.png (903x911) |
| VPC CDT-VMAX Throttle | 3344:0505 | virpil_cdt_vmax_throttle.json | virpil_mongoost50cm2_throttle.png (shared) |
| VPC ACE Pedals | 3344:0601 | virpil_ace_pedals.json | virpil_ace_torq6.png (1267x609) |
| VPC Rotor TCS Plus | 3344:0701 | — | no image yet |
| VPC Control Panel #1 | 3344:0801 | virpil_control_panel_1.json | virpil_control_panel_1.png (928x648) |
| VPC Control Panel #2 | 3344:0802 | virpil_control_panel_2.json | virpil_control_panel_2.png (1275x1142) |
| VPC Control Panel #3 | 3344:0803 | virpil_control_panel_3.json | virpil_control_panel_2.png (shared, no CP3 image) |
| VPC SharKa-50 | 3344:0810 | virpil_sharka50_cp.json | virpil_sharka50.png (1127x717) |
| VPC Stick Grips | 3344:varies | via virpil_alpha_prime_r/l.json | joystick.svg (grips handled through base) |

### Additional images available (no VID:PID confirmed yet)
- `virpil_constellation_alpha_r.png` — Constellation Alpha grip
- `virpil_constellation_alpha_prime_r.png` / `_r_b.png` — Alpha Prime grip
- `virpil_alpha_grip.png` — Alpha / Delta style grip
- `virpil_mongoost50cm2_throttle_b.png` / `_3lev.png` — throttle alternate views
- `virpil_throttle_cm2_grip.png` / `virpil_throttle_cm3_grip.png` — handle-only views
- `virpil_throttle_stick_panel.png` — throttle with side stick
- `virpil_ace2_throttle.png` — ACE-2 throttle base
- `virpil_sharka50_heels.png` / `_compact.png` — pedal variants
- `virpil_ace_torq6_front.png` / `_b.png` — pedal alternate views
- `virpil_ace2_pedals.png` — ACE-2 pedals
- `virpil_rudder_mk4.png` — Rudder Mk.IV
- `virpil_rudder_compact.png` — compact rudder bar

---

## VKB — VID `0x231D`

| Device | VID:PID | Map JSON | Image |
|--------|---------|----------|-------|
| VKB Gladiator K | 231D:0100 | vkb_gladiator_k.json | vkb_gnx_evo_scg_r.png (placeholder) |
| VKB Gunfighter Mk.II | 231D:0120 | vkb_gunfighter_mk2.json | vkb_mcg.png (478x891) |
| VKB Gunfighter Mk.III | 231D:0121 | vkb_gunfighter_mk3.json | vkb_mcg.png (478x891) |
| VKB Gunfighter Mk.IV | 231D:0122 | vkb_gunfighter_mk4.json | vkb_mcgu.png (467x914) |
| VKB Gladiator NXT | 231D:0200 | vkb_gladiator_nxt.json | vkb_gnx_evo_scg_r.png (630x766) |
| VKB Gladiator NXT EVO | 231D:0201 | vkb_gladiator_nxt_evo_r.json | vkb_gnx_evo_scg_r.png (630x766) |
| VKB GNX THQ Throttle | 231D:0300 | vkb_gnx_thq.json | vkb_stecs_standard_r.png (560x637) |
| VKB GNX SEM Module | 231D:0301 | vkb_gnx_sem.json | vkb_stecs_mini_r.png (694x931) |
| VKB GNX FSM Module | 231D:0302 | vkb_gnx_fsm.json | vkb_stecs_mini_r.png (shared) |
| VKB T-Rudder Mk.IV | 231D:0400 | vkb_t_rudder_mk4.json | vkb_t_rudder_mk5.png (1064x647) |
| VKB T-Rudder Mk.V | 231D:0401 | vkb_t_rudder_mk5.json | vkb_t_rudder_mk5.png (1064x647) |

### Additional images available (no specific map yet)
- `vkb_gnx_evo_scg_l.png` — GNX EVO SCG left-hand variant (634x771)
- `vkb_gnx_evo_scg_premium_r.png` — SCG Premium right (448x766)
- `vkb_gnx_evo_omni_throttle_r.png` — Omni Throttle right (524x762)
- `vkb_scg_r_premium.png` — SCG-R Premium for GNX EVO (566x1034)
- `vkb_stecs_mini_l.png` / `vkb_stecs_mini_r.png` — STECS Mini left/right
- `vkb_stecs_mini_premium_r.png` — STECS Mini Premium right

---

## Getting VID:PID from hardware

1. Plug in device
2. **Device Manager** -> Universal Serial Bus devices -> right-click -> Properties -> Details -> Hardware IDs
3. Note `VID_XXXX&PID_YYYY` — strip the `VID_`/`PID_` prefix and use `XXXX:YYYY` in the JSON

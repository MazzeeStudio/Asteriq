# Device Image Reference

Silhouette images and device map JSON files for Virpil and VKB hardware.
Images live in `src/Asteriq/Images/Devices/`.
Device maps live in `src/Asteriq/Images/Devices/Maps/`.

When a device is plugged in, Asteriq matches its VID:PID to a device map JSON, which points to the silhouette image.

---

## Virpil — VID `0x3344`

### Joystick Bases

| Device | VID:PID | Map JSON | Image |
|--------|---------|----------|-------|
| VPC MongoosT-50CM2 Base | 3344:0401 | virpil_mongoost50cm2_base.json | joystick.svg (no image yet) |
| VPC WarBRD Base | 3344:0402 | virpil_warbrd_base.json | joystick.svg (no image yet) |
| VPC WarBRD-D Base | 3344:0403 | virpil_warbrdd_base.json | joystick.svg (no image yet) |
| VPC FLNKR Base | 3344:???? | — | virpil_flnkr.png (471x860) — **PID needed** |

### Throttles

| Device | VID:PID | Map JSON | Image |
|--------|---------|----------|-------|
| VPC MongoosT-50CM3 Throttle | 3344:0501 | virpil_mongoost50cm3_throttle.json | virpil_mongoost_50cm3_throttle.png (903x911) |
| VPC CDT-VMAX Throttle | 3344:0505 | virpil_cdt_vmax_throttle.json | virpil_cdt_vmax_throttle.png (925x933) |
| VPC VMAX Prime Throttle | 3344:???? | — | virpil_vmax_prime_throttle.png (906x870) — **PID needed** |
| VPC CDT-Aero (Right) | 3344:???? | — | virpil_cdt-aero_r.png (724x1223) — **PID needed** |
| VPC CDT-Aero (Left) | 3344:???? | — | virpil_cdt-aero_l.png (723x1223) — **PID needed** |

### Rudder Pedals

| Device | VID:PID | Map JSON | Image |
|--------|---------|----------|-------|
| VPC R1 Falcon Rudder Pedals | 3344:0601 | virpil_ace_pedals.json | virpil_r1_falcon_rudder_pedals.png (1267x609) |
| VPC R1 Falcon Rudder Pedals (alt view) | — | — | virpil_r1_falcon_rudder_pedals_2.png (1190x471) |
| VPC WarBRD Rudder Pedals | 3344:???? | — | virpil_warbrd_rudder_pedals.png (1280x448) — **PID needed** |
| VPC ACE TORQ Rudder Pedals | 3344:???? | — | virpil_ace_torq_rudder_pedals.png (999x758) — **PID needed** |
| VPC ACE Collection Rudder Pedals | 3344:???? | — | virpil_ace_collection_rudder_pedals.png (1127x717) — **PID needed** |
| VPC ACE Interceptor Rudder Pedals | 3344:???? | — | virpil_ace_interceptor_rudder_pedals.png (1239x712) — **PID needed** |
| VPC ACE-2 Rudder Pedals | 3344:???? | — | virpil_ace2_pedals.png (1162x742) — **PID needed** |
| VPC SharKa-50 Compact Pedals | — | — | virpil_sharka50_compact.png (1250x852) |

### Collectives

| Device | VID:PID | Map JSON | Image |
|--------|---------|----------|-------|
| VPC Hawk-60 Collective | 3344:???? | — | virpil_hawk_60-collective.png (1098x833) — **PID needed** |
| VPC SharKa-50 Collective | 3344:???? | — | virpil_sharka-50-collective.png (975x962) — **PID needed** |
| VPC Dual SF Collective | 3344:???? | — | virpil_dual_sf_collective.png (1259x841) — **PID needed** |

### Control Panels

| Device | VID:PID | Map JSON | Image |
|--------|---------|----------|-------|
| VPC Control Panel #1 | 3344:0801 | virpil_control_panel_1.json | virpil_control_panel_1.png (928x648) |
| VPC Control Panel #2 | 3344:0802 | virpil_control_panel_2.json | virpil_control_panel_2.png (1254x1172) |
| VPC Control Panel #3 | 3344:0803 | virpil_control_panel_3.json | virpil_control_panel_3.png (924x805) |
| VPC SharKa-50 Control Panel | 3344:0810 | virpil_sharka50_cp.json | virpil_sharka_50_control_panel.png (1275x1142) |

### Grip Images (used via base map, no direct VID:PID)

| Image | Notes |
|-------|-------|
| virpil_constellation_alpha_r.png (458x808) | Constellation Alpha grip |
| virpil_constellation_alpha_prime_r.png (521x921) | Alpha Prime grip |

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

### Additional VKB images available (no specific map yet)
- `vkb_gnx_evo_scg_l.png` — GNX EVO SCG left-hand variant (634x771)
- `vkb_gnx_evo_scg_premium_r.png` — SCG Premium right (448x766)
- `vkb_gnx_evo_omni_throttle_r.png` — Omni Throttle right (524x762)
- `vkb_scg_r_premium.png` — SCG-R Premium for GNX EVO (566x1034)
- `vkb_stecs_mini_l.png` / `vkb_stecs_mini_r.png` — STECS Mini left/right
- `vkb_stecs_mini_premium_r.png` — STECS Mini Premium right

---

## PIDs needed — Virpil newer devices

The following devices have images but no confirmed VID:PID. Check Device Manager
(`Universal Serial Bus devices` → right-click → Properties → Details → Hardware IDs)
or the [Virpil forum](https://virpil-controls.eu/forum) for each device.

| Device | Expected PID range |
|--------|--------------------|
| VPC FLNKR Base | 3344:04xx |
| VPC VMAX Prime Throttle | 3344:05xx |
| VPC CDT-Aero R | 3344:05xx |
| VPC CDT-Aero L | 3344:05xx |
| VPC WarBRD Rudder Pedals | 3344:06xx |
| VPC ACE TORQ Rudder Pedals | 3344:06xx |
| VPC ACE Collection Rudder Pedals | 3344:06xx |
| VPC ACE Interceptor Rudder Pedals | 3344:06xx |
| VPC ACE-2 Rudder Pedals | 3344:06xx |
| VPC Hawk-60 Collective | 3344:07xx |
| VPC SharKa-50 Collective | 3344:07xx |
| VPC Dual SF Collective | 3344:07xx |

---

## Getting VID:PID from hardware

1. Plug in device
2. **Device Manager** → Universal Serial Bus devices → right-click → Properties → Details → Hardware IDs
3. Note `VID_XXXX&PID_YYYY` — strip the `VID_`/`PID_` prefix and use `XXXX:YYYY` in the JSON

# Farmverse UI handoff

Source: the **Farmverse UI** design canvas. Tokens: `farmverse-tokens.uss` (USS variables) and `farmverse-tokens.json`.

## Screen → route map

Routes marked *in code* exist in `Routes.cs`; *proposed* ids follow the same naming and still need a `Route` entry.

| Board | Screen | Route | Status | State |
|---|---|---|---|---|
| `Main` | 01 Splash | `boot` | in code |  |
| `S02_Loading` | 02 Loading and update check | `boot` | in code |  |
| `S03_Legal` | 03 Legal and consent | `onboarding` | in code |  |
| `S04_SignIn` | 04 Sign in / create account | `onboarding` | in code |  |
| `S05_Story` | 05 Welcome story (1 of 3) | `onboarding` | in code |  |
| `S06_Role` | 06 Role selection | `onboarding` | in code |  |
| `S07_Profile` | 07 Profile setup | `onboarding` | in code |  |
| `S08_Tutorial` | 08 Guided tutorial overlay | `onboarding` | in code |  |
| `S09_Starter` | 09 Starter pack reward | `onboarding` | in code |  |
| `F10_Home` | 10 Home · landing | `home` | in code |  |
| `F00_Farm` | Gameplay · your farm | `home` | in code |  |
| `F11_TileMenu` | 11 Tile action menu | `world/slot` | in code |  |
| `F12_SeedPicker` | 12 Seed picker | `world/seeds` | in code |  |
| `F13_CropProgress` | 13 Crop progress popup | `world/slot` | in code |  |
| `F14_Harvest` | 14 Harvest result | `world/slot` | in code |  |
| `F15_Build` | 15 Build mode | `world/build` | proposed |  |
| `F20_ToolRepair` | 20 Tool durability and repair | `tools/repair` | proposed |  |
| `M18_Storage` | 18 Inventory and storage | `inventory` | in code |  |
| `M19_ItemDetail` | 19 Item detail | `inventory/item` | in code |  |
| `M16_LandMap` | 16 Land expansion map | `land/map` | proposed |  |
| `M17_ExpandConfirm` | 17 Expansion confirm | `dialog/confirm` | in code |  |
| `M21_Pen` | 21 Animal pen view | `production/pen` | proposed |  |
| `M22_Feed` | 22 Feed and care | `production/feed` | proposed |  |
| `M23_Mill` | 23 Processing station | `production/machine` | in code |  |
| `M24_Ready` | 24 Production complete | `production/machine` | in code |  |
| `M25_Eat` | 25 Food need / eat | `food/eat` | proposed |  |
| `M26_WelcomeBack` | 26 Welcome back summary | `welcome-back` | in code |  |
| `P27_Shop` | 27 Shop home | `shop` | in code |  |
| `P28_Tools` | 28 Shop · tools | `shop` | in code |  |
| `P29_Machinery` | 29 Shop · machinery | `shop` | in code |  |
| `P30_Structures` | 30 Shop · structures and land | `shop` | in code |  |
| `P31_Confirm` | 31 Purchase confirm | `dialog/confirm` | in code |  |
| `P32_NoCoins` | 32 Not enough coins | `dialog/confirm` | in code |  |
| `K33_Market` | 33 Marketplace browse | `market` | in code |  |
| `K34_Filter` | 34 Search and filter | `market` | in code |  |
| `K35_Listing` | 35 Listing detail | `market/listing` | in code |  |
| `K36_BuyConfirm` | 36 Buy confirm | `dialog/confirm` | in code |  |
| `K37_Success` | 37 Purchase success | `market/listing` | in code |  |
| `K38_CreateListing` | 38 Create listing | `market/create` | in code |  |
| `K39_MyListings` | 39 My listings | `market/mine` | in code |  |
| `K40_Seller` | 40 Seller profile | `social/seller` | proposed |  |
| `K41_Visit` | 41 Visit world (read-only) | `social/visit` | in code |  |
| `K42_History` | 42 Transaction history | `orders` | in code |  |
| `K43_Promote` | 43 Promote listing | `market/promote` | proposed |  |
| `R44_Leaderboard` | 44 Leaderboard | `social/leaderboard` | in code |  |
| `R45_PlayerCard` | 45 Player card | `social/leaderboard` | in code |  |
| `R46_EditProfile` | 46 Edit profile | `profile/edit` | proposed |  |
| `R47_Daily` | 47 Daily bonus | `rewards/daily` | in code |  |
| `R48_AdOffer` | 48 Rewarded ad offer | `rewards/ad` | proposed |  |
| `R49_AdReward` | 49 Ad reward result | `rewards/ad` | proposed |  |
| `R50_Inbox` | 50 Notifications centre | `inbox` | in code |  |
| `Y51_Wallet` | 51 Wallet | `wallet` | proposed |  |
| `Y52_Settings` | 52 Settings | `settings` | in code |  |
| `Y53_Language` | 53 Language picker | `settings/language` | proposed |  |
| `Y54_Help` | 54 Help and contact | `settings/help` | proposed |  |
| `Y55_Account` | 55 Account management | `settings/account` | proposed |  |
| `Y56_Offline` | 56 Connection lost | `system/offline` | proposed |  |
| `Y57_Maintenance` | 57 Maintenance / update required | `system/maintenance` | proposed |  |
| `Y58_Confirm` | 58 Generic confirm and error | `dialog/confirm` | in code |  |
| `A59_Facilities` | 59 Facilities hub | `facilities` | proposed |  |
| `A60_Detail` | 60 Facility detail and upgrade | `facilities/detail` | proposed |  |
| `A61_UpConfirm` | 61 Upgrade confirm | `dialog/confirm` | in code |  |
| `A62_Building` | 62 Construction in progress | `facilities/detail` | proposed |  |
| `A63_UpDone` | 63 Upgrade complete | `facilities/detail` | proposed |  |
| `A64_Well` | 64 Well panel · water | `facilities/well` | proposed |  |
| `A65_Warehouse` | 65 Warehouse storage panel | `facilities/warehouse` | proposed |  |
| `A66_Full` | 66 Capacity full | `facilities/full` | proposed |  |
| `A67_Farmhouse` | 67 Farmhouse · homestead | `facilities/farmhouse` | proposed |  |
| `A68_Stall` | 68 Market stall manager | `facilities/stall` | proposed |  |
| `A69_Granary` | 69 Granary and raw storehouse | `facilities/granary` | proposed |  |
| `A70_Cellar` | 70 Cellar · finished goods | `facilities/cellar` | proposed |  |
| `A71_Rating` | 71 Farm rating | `facilities/rating` | proposed |  |
| `S05b_Story2` | 5b Story page 2 | `onboarding` | in code |  |
| `S05c_Story3` | 5c Story page 3 | `onboarding` | in code |  |
| `F11b_TileReady` | 11b Tile menu · crop ready | `world/slot` | in code |  |
| `F11c_TileLocked` | 11c Tile menu · locked tile | `world/slot` | in code |  |
| `F15b_BuildEmpty` | 15b Build tray · empty | `world/build` | proposed |  |
| `E72_Empire` | 72 Empire map | `empire` | proposed |  |
| `E73_Switcher` | 73 Plot switcher | `empire/switch` | proposed |  |
| `E74_PlotDetail` | 74 Plot detail | `empire/plot` | proposed |  |
| `E75_NewPlots` | 75 Shop · new plots | `empire/shop` | proposed |  |
| `E76_PlotPreview` | 76 Plot preview | `empire/preview` | proposed |  |
| `E77_PlotConfirm` | 77 Plot purchase confirm | `dialog/confirm` | in code |  |
| `E78_PlotType` | 78 Choose a plot type | `empire/setup` | proposed |  |
| `E79_Welcome` | 79 Welcome to your new plot | `empire/welcome` | proposed |  |
| `E80_Progress` | 80 Empire progress and titles | `empire/titles` | proposed |  |
| `E81_Dashboard` | 81 Empire dashboard | `empire/dashboard` | proposed |  |
| `M18L_StorageLoading` | Storage · loading | `inventory` | in code | loading |
| `M18E_StorageEmpty` | Storage · empty | `inventory` | in code | empty |
| `M18X_StorageError` | Storage · error | `inventory` | in code | error |
| `M18O_StorageOffline` | Storage · offline | `inventory` | in code | offline |
| `K33L_MarketLoading` | Market · loading | `market` | in code | loading |
| `K33E_MarketEmpty` | Market · empty | `market` | in code | empty |
| `K33X_MarketError` | Market · error | `market` | in code | error |
| `K33O_MarketOffline` | Market · offline | `market` | in code | offline |
| `R50L_InboxLoading` | Inbox · loading | `inbox` | in code | loading |
| `R50E_InboxEmpty` | Inbox · empty | `inbox` | in code | empty |
| `R50X_InboxError` | Inbox · error | `inbox` | in code | error |
| `R50O_InboxOffline` | Inbox · offline | `inbox` | in code | offline |
| `R44L_LeaderboardLoading` | Leaderboard · loading | `social/leaderboard` | in code | loading |
| `R44E_LeaderboardEmpty` | Leaderboard · empty | `social/leaderboard` | in code | empty |
| `R44X_LeaderboardError` | Leaderboard · error | `social/leaderboard` | in code | error |
| `R44O_LeaderboardOffline` | Leaderboard · offline | `social/leaderboard` | in code | offline |

## Assets

Painted art lives in `Assets/Farmverse/Content/UI/Art/Painted`. Sizes below are the web-preview exports; import the 1024 px masters as sprites.

### Animals (3)

`animal_chicken.png` 256×256, `animal_cow.png` 256×256, `animal_sheep.png` 256×256

### Dialog art (8)

`art_construction.jpg` 900×600, `art_daily_bonus.jpg` 900×589, `art_new_plot.jpg` 900×586, `art_not_enough_coins.jpg` 900×576, `art_rewarded_ad.jpg` 900×575, `art_starter_pack_crop.jpg` 900×572, `art_upgrade_complete.jpg` 900×581, `art_welcome_back.jpg` 900×579

### Avatars (10)

`avatar_01.png` 384×384, `avatar_02.png` 384×384, `avatar_03.png` 384×384, `avatar_04.png` 384×384, `avatar_05.png` 384×384, `avatar_06.png` 384×384, `avatar_07.png` 256×256, `avatar_08.png` 256×256, `avatar_09.png` 256×256, `avatar_10.png` 256×256

### Backgrounds (7)

`bg_empire_map.jpg` 780×1202, `bg_home_farmer.jpg` 780×1170, `bg_loading.jpg` 780×1170, `bg_maintenance.jpg` 780×1170, `bg_offline.jpg` 780×1170, `bg_onboarding.jpg` 780×1170, `bg_splash.jpg` 780×1170

### Coin bundles (3)

`bundle_coins_l.png` 256×256, `bundle_coins_m.png` 256×256, `bundle_coins_s.png` 384×384

### Chests (3)

`chest_gold.png` 256×256, `chest_silver.png` 256×256, `chest_wood.png` 256×256

### Coach (3)

`coach_cheer.png` 512×512, `coach_point.png` 512×512, `coach_wave.png` 512×512

### Crops (7)

`crop_beans.png` 256×256, `crop_maize.png` 256×256, `crop_onion.png` 256×256, `crop_potato.png` 256×256, `crop_sunflower.png` 256×256, `crop_tomato.png` 384×384, `crop_wheat.png` 256×256

### Empty states (7)

`empty_friends.png` 288×300, `empty_listings.png` 256×256, `empty_locked.png` 189×256, `empty_notifications.png` 287×300, `empty_offline.png` 298×300, `empty_search.png` 255×300, `empty_storage.png` 256×256

### Facilities (16)

`fac_cellar_l1.png` 256×256, `fac_cellar_l2.png` 256×256, `fac_chicken_coop_l1.png` 256×256, `fac_chicken_coop_l2.png` 256×256, `fac_farmhouse_l1.png` 256×256, `fac_farmhouse_l3.png` 256×256, `fac_fence_pack_l1.png` 256×256, `fac_granary_l1.png` 256×256, `fac_granary_l2.png` 256×256, `fac_stall_l1.png` 256×256, `fac_stall_l2.png` 256×256, `fac_storage_shed_l1.png` 256×256, `fac_warehouse_l1.png` 256×256, `fac_warehouse_l3.png` 256×256, `fac_well_l1.png` 256×256, `fac_well_l2.png` 256×256

### Goods (5)

`good_bread.png` 256×256, `good_cheese.png` 256×256, `good_flour.png` 256×256, `good_oil.png` 256×256, `good_preserves.png` 256×256

### Header art (5)

`hdr_facilities.jpg` 900×600, `hdr_market.jpg` 900×695, `hdr_pen.jpg` 900×600, `hdr_shop.jpg` 900×600, `hdr_workshop.jpg` 900×600

### HUD icons (6)

`hud_daily_bonus.png` 256×256, `hud_empire_map.png` 256×256, `hud_facilities.png` 256×256, `hud_market.png` 256×256, `hud_notifications.png` 256×256, `hud_rewarded_ad.png` 256×256

### Icons (34)

`ico_account.png` 384×384, `ico_ad_tv.png` 256×256, `ico_back.png` 256×256, `ico_bell.png` 256×256, `ico_check.png` 384×384, `ico_chest.png` 256×256, `ico_clock.png` 256×256, `ico_close.png` 384×384, `ico_coin.png` 384×384, `ico_coin_pile.png` 256×256, `ico_coin_stack.png` 256×256, `ico_filter.png` 256×256, `ico_food.png` 256×256, `ico_gift.png` 384×384, `ico_home_barn.png` 256×256, `ico_hourglass.png` 256×256, `ico_info.png` 384×384, `ico_inventory.png` 256×256, `ico_lock.png` 384×384, `ico_mail.png` 256×256, `ico_map.png` 256×256, `ico_market.png` 256×256, `ico_megaphone.png` 256×256, `ico_minus.png` 256×256, `ico_money_bag.png` 256×256, `ico_plus.png` 256×256, `ico_ranks_trophy.png` 256×256, `ico_search.png` 256×256, `ico_settings_gear.png` 256×256, `ico_shop.png` 256×256, `ico_star.png` 384×384, `ico_trash.png` 256×256, `ico_warning.png` 256×256, `ico_water_drop.png` 384×384

### Land tiles (2)

`land_hex.png` 256×256, `land_hex_locked.png` 256×256

### Logos (3)

`logo_apple_white.png` 81×96, `logo_google2.png` 94×96, `logo_wordmark.png` 512×512

### Machines (5)

`mach_food_factory.png` 256×256, `mach_mill.png` 256×256, `mach_oil_press.png` 256×256, `mach_packer.png` 256×256, `mach_seed_miner.png` 256×256

### Medals (3)

`medal_bronze.png` 256×256, `medal_gold.png` 256×256, `medal_silver.png` 256×256

### Plot types (4)

`plot_field.png` 256×256, `plot_mine.png` 256×256, `plot_pasture.png` 248×256, `plot_workshop.png` 227×256

### Plot previews (4)

`preview_field.jpg` 900×600, `preview_mine.jpg` 900×600, `preview_pasture.jpg` 900×600, `preview_workshop.jpg` 900×600

### Animal products (4)

`prod_egg.png` 256×256, `prod_meat.png` 256×256, `prod_milk.png` 256×256, `prod_wool.png` 256×256

### Resources (7)

`res_charcoal.png` 256×256, `res_fertilizer.png` 256×256, `res_grain.png` 256×256, `res_hay.png` 256×256, `res_manure.png` 256×256, `res_oilcake.png` 256×256, `res_wood.png` 256×256

### Roles (4)

`role_farmer.png` 512×512, `role_manufacturer.png` 512×512, `role_rearer.png` 512×512, `role_seed_miner.png` 512×512

### Facility scenes (8)

`scene_cellar.jpg` 900×577, `scene_chicken_coop.jpg` 900×573, `scene_farmhouse.jpg` 900×600, `scene_granary.jpg` 900×580, `scene_stall.jpg` 900×585, `scene_storage_shed.jpg` 900×582, `scene_warehouse.jpg` 900×600, `scene_well.jpg` 900×572

### Other (1)

`season_spring.png` 256×256

### Seeds (7)

`seed_beans.png` 256×256, `seed_maize.png` 256×256, `seed_onion.png` 256×256, `seed_potato.png` 256×256, `seed_sunflower.png` 256×256, `seed_tomato.png` 384×384, `seed_wheat.png` 256×256

### Shop art (5)

`shop_cat_land.png` 320×320, `shop_cat_machinery.png` 320×320, `shop_cat_structures.png` 320×320, `shop_cat_tools.png` 320×320, `shop_featured_banner.jpg` 900×607

### Story (3)

`story_1_land.jpg` 780×1170, `story_2_role.jpg` 780×1170, `story_3_harvest.jpg` 780×1170

### Titles (5)

`title_1_sprout.png` 256×256, `title_2_grower.png` 256×256, `title_3_farmer.png` 256×256, `title_4_rancher.png` 255×256, `title_5_tycoon.png` 256×256

### Tools (7)

`tool_axe.png` 256×256, `tool_hoe_broken.png` 256×256, `tool_shovel.png` 256×256, `tool_sickle.png` 256×256, `tool_sickle_broken.png` 256×256, `tool_watering_can.png` 384×384, `tool_watering_can_broken.png` 256×256

## Rules

- Outline text only, never outline plus shadow.
- Labels never wrap; pairs of buttons share the row at 20 pt.
- A close X replaces Cancel.
- 3D icons get no plate.
- Art fills its frame; no baked borders.
- Coins only, never real-money prices.
- Every list screen ships loading, empty, error and offline.

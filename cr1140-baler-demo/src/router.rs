// SPDX-License-Identifier: GPL-3.0-only
//! Screen router — pure navigation state machine (issue 02).

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Screen {
    Menu,
    /// Combined operator view: bale counter + wrapping control on one screen.
    Dashboard,
    BaleCounter,
    Knives,
    Wrapping,
    /// System telemetry (CPU/mem/temp/uptime/network) + backlight control.
    Telemetry,
}

/// Keypad navigation events the router understands. Mapped in main.rs:
/// Up/Down/Enter from the d-pad+Enter, Back from F6.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Nav {
    Up,
    Down,
    Enter,
    Back,
}

/// Side effect of handling a nav event.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Effect {
    None,
    Exit,
}

/// Fixed home-menu order. Index maps to the sub-screen opened on `Nav::Enter`:
/// 0 → Dashboard, 1 → BaleCounter, 2 → Knives, 3 → Wrapping, 4 → Telemetry.
const MENU_ENTRIES: [&str; 5] = [
    "Dashboard",
    "Bale Counter",
    "Knives",
    "Wrapping",
    "Telemetry",
];

/// Maps a menu cursor index onto the sub-screen it opens. Indices outside the
/// menu range cannot occur (the cursor is kept in `0..MENU_ENTRIES.len()` by
/// the wrapping navigation), so an out-of-range index falls back to `Menu`.
fn screen_for_index(index: usize) -> Screen {
    match index {
        0 => Screen::Dashboard,
        1 => Screen::BaleCounter,
        2 => Screen::Knives,
        3 => Screen::Wrapping,
        4 => Screen::Telemetry,
        _ => Screen::Menu,
    }
}

pub struct Router {
    screen: Screen,
    cursor: usize,
}

impl Router {
    pub fn new() -> Self {
        Router {
            screen: Screen::Menu,
            cursor: 0,
        }
    }

    pub fn screen(&self) -> Screen {
        self.screen
    }

    pub fn menu_cursor(&self) -> usize {
        self.cursor
    }

    pub fn menu_entries() -> &'static [&'static str] {
        &MENU_ENTRIES
    }

    pub fn screen_title(&self) -> &'static str {
        match self.screen {
            Screen::Menu => "Baler",
            Screen::Dashboard => MENU_ENTRIES[0],
            Screen::BaleCounter => MENU_ENTRIES[1],
            Screen::Knives => MENU_ENTRIES[2],
            Screen::Wrapping => MENU_ENTRIES[3],
            Screen::Telemetry => MENU_ENTRIES[4],
        }
    }

    pub fn handle(&mut self, nav: Nav) -> Effect {
        match self.screen {
            Screen::Menu => self.handle_menu(nav),
            _ => self.handle_subscreen(nav),
        }
    }

    fn handle_subscreen(&mut self, nav: Nav) -> Effect {
        match nav {
            // F6 on a sub-screen is "Back": return to the menu. The cursor is
            // left untouched so it still points at the entry that was opened.
            Nav::Back => {
                self.screen = Screen::Menu;
                Effect::None
            }
            // Up/Down/Enter belong to the feature module on a sub-screen, so the
            // router treats them as no-ops here.
            _ => Effect::None,
        }
    }

    fn handle_menu(&mut self, nav: Nav) -> Effect {
        match nav {
            // Cursor navigation is WRAPPING (not clamping): Down past the last
            // entry wraps to the first, Up past the first wraps to the last.
            // Modular arithmetic keeps the cursor in `0..MENU_ENTRIES.len()`.
            Nav::Down => {
                self.cursor = (self.cursor + 1) % MENU_ENTRIES.len();
                Effect::None
            }
            Nav::Up => {
                self.cursor = (self.cursor + MENU_ENTRIES.len() - 1) % MENU_ENTRIES.len();
                Effect::None
            }
            Nav::Enter => {
                self.screen = screen_for_index(self.cursor);
                Effect::None
            }
            // F6 on the menu is "Exit"; the screen is left unchanged so the
            // caller decides what exiting means.
            Nav::Back => Effect::Exit,
        }
    }
}

impl Default for Router {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn new_starts_at_menu_cursor_zero() {
        let r = Router::new();
        assert_eq!(r.screen(), Screen::Menu);
        assert_eq!(r.menu_cursor(), 0);
    }

    #[test]
    fn menu_entries_are_fixed_order() {
        assert_eq!(
            Router::menu_entries(),
            [
                "Dashboard",
                "Bale Counter",
                "Knives",
                "Wrapping",
                "Telemetry"
            ]
        );
    }

    #[test]
    fn down_moves_menu_cursor_forward() {
        let mut r = Router::new();
        assert_eq!(r.handle(Nav::Down), Effect::None);
        assert_eq!(r.menu_cursor(), 1);
        assert_eq!(r.screen(), Screen::Menu);
    }

    #[test]
    fn down_wraps_from_last_to_first() {
        let mut r = Router::new();
        for _ in 0..4 {
            r.handle(Nav::Down); // 0 -> 1 -> 2 -> 3 -> 4 (last)
        }
        assert_eq!(r.menu_cursor(), 4);
        r.handle(Nav::Down); // 4 -> 0 (wrap)
        assert_eq!(r.menu_cursor(), 0);
    }

    #[test]
    fn up_wraps_from_first_to_last() {
        let mut r = Router::new();
        assert_eq!(r.menu_cursor(), 0);
        assert_eq!(r.handle(Nav::Up), Effect::None); // 0 -> 4 (wrap)
        assert_eq!(r.menu_cursor(), 4);
        r.handle(Nav::Up); // 4 -> 3
        assert_eq!(r.menu_cursor(), 3);
        assert_eq!(r.screen(), Screen::Menu);
    }

    #[test]
    fn enter_opens_screen_at_cursor() {
        for (down_presses, expected) in [
            (0, Screen::Dashboard),
            (1, Screen::BaleCounter),
            (2, Screen::Knives),
            (3, Screen::Wrapping),
            (4, Screen::Telemetry),
        ] {
            let mut r = Router::new();
            for _ in 0..down_presses {
                r.handle(Nav::Down);
            }
            assert_eq!(r.handle(Nav::Enter), Effect::None);
            assert_eq!(r.screen(), expected);
        }
    }

    #[test]
    fn back_on_menu_exits_and_stays_on_menu() {
        let mut r = Router::new();
        assert_eq!(r.handle(Nav::Back), Effect::Exit);
        assert_eq!(r.screen(), Screen::Menu);
    }

    #[test]
    fn back_on_subscreen_returns_to_menu() {
        let mut r = Router::new();
        r.handle(Nav::Enter); // open Dashboard (cursor 0)
        assert_eq!(r.screen(), Screen::Dashboard);
        assert_eq!(r.handle(Nav::Back), Effect::None);
        assert_eq!(r.screen(), Screen::Menu);
    }

    #[test]
    fn back_preserves_menu_cursor() {
        let mut r = Router::new();
        r.handle(Nav::Down); // cursor -> 1 (Bale Counter)
        r.handle(Nav::Enter); // open Bale Counter
        assert_eq!(r.screen(), Screen::BaleCounter);
        r.handle(Nav::Back); // back to menu
        assert_eq!(r.screen(), Screen::Menu);
        assert_eq!(r.menu_cursor(), 1, "cursor stays on the opened entry");
    }

    #[test]
    fn subscreen_up_down_enter_are_noops() {
        let mut r = Router::new();
        r.handle(Nav::Enter); // open Dashboard (cursor 0)
        for nav in [Nav::Up, Nav::Down, Nav::Enter] {
            assert_eq!(r.handle(nav), Effect::None, "{nav:?} is a no-op");
            assert_eq!(r.screen(), Screen::Dashboard, "{nav:?} left screen alone");
        }
    }

    #[test]
    fn screen_title_is_baler_on_menu() {
        let r = Router::new();
        assert_eq!(r.screen_title(), "Baler");
    }

    #[test]
    fn screen_title_matches_opened_entry_label() {
        for (down_presses, expected) in [
            (0, "Dashboard"),
            (1, "Bale Counter"),
            (2, "Knives"),
            (3, "Wrapping"),
            (4, "Telemetry"),
        ] {
            let mut r = Router::new();
            for _ in 0..down_presses {
                r.handle(Nav::Down);
            }
            r.handle(Nav::Enter);
            assert_eq!(r.screen_title(), expected);
        }
    }

    #[test]
    fn default_starts_like_new() {
        let d = Router::default();
        assert_eq!(d.screen(), Screen::Menu);
        assert_eq!(d.menu_cursor(), 0);
        assert_eq!(d.screen_title(), "Baler");
    }
}

#[test]
fn ping_returns_easykatago() {
    assert_eq!(crate::commands::ping(), "easykatago");
}

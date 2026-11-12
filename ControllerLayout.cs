using System;

namespace PadDesk {
    // Calibration labels remain compatible with existing saved profiles.
    public static class ControllerLayout {
        public static string Key(string name) {
            switch((name??"").Trim().ToUpperInvariant()) {
                case "A":case "B":case "X":case "Y":case "LB":case "RB":case "LT":case "RT":
                case "UP":case "DOWN":case "LEFT":case "RIGHT":case "LS":case "RS":return name.Trim().ToUpperInvariant();
                case "L1":case "L1 / LB":return "LB";
                case "R1":case "R1 / RB":return "RB";
                case "L2":case "L2 / LT":return "LT";
                case "R2":case "R2 / RT":return "RT";
                case "返回 / SELECT":case "VIEW":case "SELECT":return "View";
                case "开始 / START":case "MENU":case "START":return "Menu";
                case "左摇杆按下":return "LS";
                case "右摇杆按下":return "RS";
                case "HOME":case "XBOX":return "Home";
                case "CAPTURE":case "SHARE":return "Share";
                case "方向上":return "UP";case "方向下":return "DOWN";
                case "方向左":return "LEFT";case "方向右":return "RIGHT";
                default:return null;
            }
        }
        public static string KeyFor(Mapping mapping) {
            if(mapping==null || mapping.Reserved)return null;
            return mapping.Locked?Key(mapping.Target):mapping.Confirmed?Key(mapping.Name):null;
        }
        public static string Caption(string key,string layout) {
            if(layout=="Photo")switch(key){case "Home":return "Home · 主页键";case "Share":return "Capture · 截图键";case "View":return "− · 减号键";case "Menu":return "+ · 加号键";}
            return Caption(key);
        }
        public static string Caption(string key) {
            switch(key) {
                case "LB":return "LB · 左肩键";case "RB":return "RB · 右肩键";
                case "LT":return "LT · 左扳机";case "RT":return "RT · 右扳机";
                case "LS":return "LS · 左摇杆按下";case "RS":return "RS · 右摇杆按下";
                case "View":return "View · 视图键";case "Menu":return "Menu · 菜单键";
                case "Home":return "Xbox · 主菜单";case "Share":return "Share · 分享键";
                case "UP":return "方向键 · 上";case "DOWN":return "方向键 · 下";
                case "LEFT":return "方向键 · 左";case "RIGHT":return "方向键 · 右";
                default:return key==null?"Xbox 标准布局":key+" · 动作键";
            }
        }
    }
}

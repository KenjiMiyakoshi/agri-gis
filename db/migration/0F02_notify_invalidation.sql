-- D'302 (WD'3): PostgreSQL LISTEN/NOTIFY 配線
-- 既存 7 関数を CREATE OR REPLACE で書き直す代わりに、TRIGGER 経由で
-- pg_notify を発行する設計を採用 (既存関数を touch せず、変更箇所最小化)。
--
-- 通知チャネル: agri_gis_layer_invalidate
-- ペイロード: { layerId, reason, styleVersion?, action?, occurredAt }
--
-- API 側 (PostgresLayerInvalidationBroker) で LISTEN し、SSE クライアントに配信。

-- ===== feature_current / feature_history への変更を通知 =====
CREATE OR REPLACE FUNCTION fn_trg_notify_feature_change() RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_layer_id INT;
    v_action   TEXT;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_layer_id := OLD.layer_id;
        v_action := 'delete';
    ELSE
        v_layer_id := NEW.layer_id;
        v_action := lower(TG_OP);
    END IF;
    PERFORM pg_notify('agri_gis_layer_invalidate',
        json_build_object(
            'layerId', v_layer_id,
            'reason', 'feature',
            'action', v_action,
            'occurredAt', now()
        )::text);
    RETURN COALESCE(NEW, OLD);
END;
$$;

DROP TRIGGER IF EXISTS trg_feature_current_notify ON feature_current;
CREATE TRIGGER trg_feature_current_notify
    AFTER INSERT OR UPDATE OR DELETE ON feature_current
    FOR EACH ROW EXECUTE FUNCTION fn_trg_notify_feature_change();

-- ===== layer_style_version への INSERT を通知 (style 更新) =====
CREATE OR REPLACE FUNCTION fn_trg_notify_style_change() RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM pg_notify('agri_gis_layer_invalidate',
        json_build_object(
            'layerId', NEW.layer_id,
            'reason', 'style',
            'styleVersion', NEW.style_version,
            'occurredAt', now()
        )::text);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_layer_style_version_notify ON layer_style_version;
CREATE TRIGGER trg_layer_style_version_notify
    AFTER INSERT ON layer_style_version
    FOR EACH ROW EXECUTE FUNCTION fn_trg_notify_style_change();

-- ===== layers (layer 自体の更新/削除) への変更を通知 =====
CREATE OR REPLACE FUNCTION fn_trg_notify_layer_change() RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_layer_id INT;
    v_action   TEXT;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_layer_id := OLD.layer_id;
        v_action := 'delete';
    ELSE
        v_layer_id := NEW.layer_id;
        v_action := lower(TG_OP);
    END IF;
    PERFORM pg_notify('agri_gis_layer_invalidate',
        json_build_object(
            'layerId', v_layer_id,
            'reason', 'layer',
            'action', v_action,
            'occurredAt', now()
        )::text);
    RETURN COALESCE(NEW, OLD);
END;
$$;

DROP TRIGGER IF EXISTS trg_layers_notify ON layers;
CREATE TRIGGER trg_layers_notify
    AFTER INSERT OR UPDATE OR DELETE ON layers
    FOR EACH ROW EXECUTE FUNCTION fn_trg_notify_layer_change();

COMMENT ON FUNCTION fn_trg_notify_feature_change IS
    'Phase D-prime D''302: feature_current への変更を pg_notify で通知 (API SSE 経由)';
COMMENT ON FUNCTION fn_trg_notify_style_change IS
    'Phase D-prime D''302: layer_style_version への INSERT を pg_notify で通知';
COMMENT ON FUNCTION fn_trg_notify_layer_change IS
    'Phase D-prime D''302: layers への変更を pg_notify で通知';

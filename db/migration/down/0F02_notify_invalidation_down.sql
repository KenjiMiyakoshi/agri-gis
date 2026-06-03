DROP TRIGGER IF EXISTS trg_feature_current_notify ON feature_current;
DROP TRIGGER IF EXISTS trg_layer_style_version_notify ON layer_style_version;
DROP TRIGGER IF EXISTS trg_layers_notify ON layers;
DROP FUNCTION IF EXISTS fn_trg_notify_feature_change();
DROP FUNCTION IF EXISTS fn_trg_notify_style_change();
DROP FUNCTION IF EXISTS fn_trg_notify_layer_change();

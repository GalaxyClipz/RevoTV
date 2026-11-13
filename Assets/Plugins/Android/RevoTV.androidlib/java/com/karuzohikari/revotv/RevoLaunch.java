package com.karuzohikari.revotv;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

/**
 * The TV's app list entry for Revo. It opens Revo as the home screen when Revo is the TV's home (a plain app start put a second
 * Revo beside the home one; the next Home press then made another and the engine, which can only run once, crashed back to the
 * HDMI input), otherwise Revo itself. Draws nothing.
 */
public class RevoLaunch extends Activity {
    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        try {
            RevoTV.ensureHome(this); // before choosing how to open Revo: as the home screen when it just became it
            Intent i = RevoTV.restartIntent(this);
            Intent from = getIntent();
            if (from != null && from.getExtras() != null) i.putExtras(from.getExtras());
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(i);
        } catch (Throwable ignored) { }
        finish();
        overridePendingTransition(0, 0);
    }
}

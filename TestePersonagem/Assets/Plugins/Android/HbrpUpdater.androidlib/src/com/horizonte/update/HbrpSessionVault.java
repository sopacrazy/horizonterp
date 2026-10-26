package com.horizonte.update;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
import java.nio.charset.StandardCharsets;

public final class HbrpSessionVault {
 private static final String ALIAS="hbrp-session-v1", PREFS="hbrp-session";
 private static SecretKey key() throws Exception {
  KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
  if(!store.containsAlias(ALIAS)) {
   KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
   generator.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT)
    .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());
   generator.generateKey();
  }
  return (SecretKey)store.getKey(ALIAS,null);
 }
 public static void write(Context context,String token) throws Exception {
  Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());
  String iv=Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP);
  String data=Base64.encodeToString(cipher.doFinal(token.getBytes(StandardCharsets.UTF_8)),Base64.NO_WRAP);
  if(!context.getSharedPreferences(PREFS,Context.MODE_PRIVATE).edit().putString("iv",iv).putString("data",data).commit())throw new java.io.IOException("Session storage unavailable");
 }
 public static String read(Context context) {
  try {
   android.content.SharedPreferences prefs=context.getSharedPreferences(PREFS,Context.MODE_PRIVATE);
   String data=prefs.getString("data",null),iv=prefs.getString("iv",null);if(data==null||iv==null)return null;
   Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Base64.decode(iv,Base64.NO_WRAP)));
   return new String(cipher.doFinal(Base64.decode(data,Base64.NO_WRAP)),StandardCharsets.UTF_8);
  } catch(Exception error){clear(context);return null;}
 }
 public static void clear(Context context){context.getSharedPreferences(PREFS,Context.MODE_PRIVATE).edit().clear().commit();}
}

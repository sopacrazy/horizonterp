package com.horizonte.update;

import android.app.Activity;
import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.pm.Signature;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.Build;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import android.provider.Settings;
import java.io.File;
import java.io.FileNotFoundException;
import java.security.MessageDigest;
import java.util.Arrays;

// Only a single validated APK in the app's private cache can be shared, read-only.
public final class HbrpApkProvider extends ContentProvider {
    private static File apk(android.content.Context context) {
        return new File(context.getCacheDir(), "hbrp-updates/update.apk");
    }
    public static String downloadPath(Activity activity) {
        File file=apk(activity);file.getParentFile().mkdirs();return file.getAbsolutePath();
    }
    private static long code(PackageInfo info) {
        return Build.VERSION.SDK_INT>=28?info.getLongVersionCode():info.versionCode;
    }
    private static String[] signers(PackageInfo info) throws Exception {
        Signature[] signatures=Build.VERSION.SDK_INT>=28?info.signingInfo.getApkContentsSigners():info.signatures;
        if(signatures==null||signatures.length==0)throw new SecurityException("APK sem assinatura");
        String[] result=new String[signatures.length];
        for(int i=0;i<result.length;i++)result[i]=Arrays.toString(MessageDigest.getInstance("SHA-256").digest(signatures[i].toByteArray()));
        Arrays.sort(result);return result;
    }
    public static int install(Activity activity,int expectedCode) throws Exception {
        PackageManager pm=activity.getPackageManager();
        int flags=Build.VERSION.SDK_INT>=28?PackageManager.GET_SIGNING_CERTIFICATES:PackageManager.GET_SIGNATURES;
        PackageInfo installed=pm.getPackageInfo(activity.getPackageName(),flags);
        PackageInfo update=pm.getPackageArchiveInfo(apk(activity).getAbsolutePath(),flags);
        if(update==null||!activity.getPackageName().equals(update.packageName)||code(update)!=expectedCode||code(update)<=code(installed))
            throw new SecurityException("Pacote ou versao de atualizacao invalida");
        if(!Arrays.equals(signers(installed),signers(update)))throw new SecurityException("Assinatura diferente do jogo instalado");
        if(!pm.canRequestPackageInstalls()) {
            activity.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+activity.getPackageName())));
            return 1;
        }
        Uri uri=Uri.parse("content://"+activity.getPackageName()+".updates/update.apk");
        Intent intent=new Intent(Intent.ACTION_VIEW).setDataAndType(uri,"application/vnd.android.package-archive");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        activity.startActivity(intent);return 0;
    }
    @Override public boolean onCreate(){return true;}
    private File checked(Uri uri) throws FileNotFoundException {
        if(!"/update.apk".equals(uri.getPath())||!uri.getAuthority().equals(getContext().getPackageName()+".updates"))
            throw new FileNotFoundException("Arquivo indisponivel");
        File file=apk(getContext());if(!file.isFile())throw new FileNotFoundException("Arquivo indisponivel");return file;
    }
    @Override public ParcelFileDescriptor openFile(Uri uri,String mode) throws FileNotFoundException {
        if(!"r".equals(mode))throw new FileNotFoundException("Somente leitura");
        return ParcelFileDescriptor.open(checked(uri),ParcelFileDescriptor.MODE_READ_ONLY);
    }
    @Override public String getType(Uri uri){return "application/vnd.android.package-archive";}
    @Override public Cursor query(Uri uri,String[] projection,String selection,String[] args,String sort) {
        try {
            File file=checked(uri);String[] columns=projection!=null?projection:new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE};
            MatrixCursor cursor=new MatrixCursor(columns);Object[] row=new Object[columns.length];
            for(int i=0;i<columns.length;i++)row[i]=OpenableColumns.DISPLAY_NAME.equals(columns[i])?"HorizonteBrasil.apk":OpenableColumns.SIZE.equals(columns[i])?file.length():null;
            cursor.addRow(row);return cursor;
        } catch(FileNotFoundException error){return null;}
    }
    @Override public Uri insert(Uri uri,ContentValues values){throw new UnsupportedOperationException();}
    @Override public int delete(Uri uri,String selection,String[] args){throw new UnsupportedOperationException();}
    @Override public int update(Uri uri,ContentValues values,String selection,String[] args){throw new UnsupportedOperationException();}
}
